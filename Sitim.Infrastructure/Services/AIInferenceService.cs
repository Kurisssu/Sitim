using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Sitim.Core.Entities;
using Sitim.Core.Services;
using Sitim.Infrastructure.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using FellowOakDicom;
using System.IO.Compression;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;

namespace Sitim.Infrastructure.Services;

public class AIInferenceService : IAIInferenceService
{
    private const int MaxDicomInstancesToInspect = 12;
    private const int MaxFramesPerInstance = 3;
    private const int MaxTotalFramesForInference = 8;

    private readonly AppDbContext _context;
    private readonly IInferenceEngine _inferenceEngine;
    private readonly IModelStorageService _modelStorage;
    private readonly IOrthancClientFactory _orthancFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AIInferenceService> _logger;

    public AIInferenceService(
        AppDbContext context,
        IInferenceEngine inferenceEngine,
        IModelStorageService modelStorage,
        IOrthancClientFactory orthancFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AIInferenceService> logger)
    {
        _context = context;
        _inferenceEngine = inferenceEngine;
        _modelStorage = modelStorage;
        _orthancFactory = orthancFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<AIAnalysisJob> AnalyzeStudyAsync(
        Guid studyId,
        Guid? modelId = null,
        CancellationToken cancellationToken = default)
    {
        // Doctor must explicitly select model
        // No auto-selection fallback allowed (violates doctor-driven design)
        if (!modelId.HasValue)
            throw new InvalidOperationException(
                "Model selection is required.");

        var sw = Stopwatch.StartNew();

        // Get current user ID from claims
        var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            throw new InvalidOperationException("User not authenticated");

        // Get user name for audit trail
        var userNameClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Name);
        var userName = userNameClaim?.Value ?? "Unknown";

        // Get study metadata
        var study = await _context.ImagingStudies
            .IgnoreQueryFilters() // Allow SuperAdmin to access any study
            .FirstOrDefaultAsync(s => s.Id == studyId, cancellationToken);

        if (study == null)
            throw new InvalidOperationException("Study not found");

        if (!study.InstitutionId.HasValue)
            throw new InvalidOperationException("Study has no institution");

        _logger.LogInformation(
            "Starting AI analysis for study {StudyId} from institution {InstitutionId}",
            studyId, study.InstitutionId);

        // Get AI model (modelId is guaranteed non-null by check above)
        var model = await _context.AIModels.FindAsync(modelId.Value)
            ?? throw new InvalidOperationException("Model not found");

        _logger.LogInformation("Using model: {ModelName} (v{Version})", model.Name, model.Version);

        // Use database metadata for logging
        // Get class names from model for audit trail
        var classNamesJson = model.ClassNames ?? "[]";
        var classNames = JsonSerializer.Deserialize<string[]>(classNamesJson) ?? Array.Empty<string>();
        var predictionClassName = classNames.Length > 0 ? string.Join(", ", classNames) : "Unknown";


        var modelBytes = await DownloadModelBytesAsync(model.StorageFileName, cancellationToken);
        var (predictionClass, confidence, probabilities, framesUsed) = await RunStudyInferenceAsync(
            study.InstitutionId.Value,
            study.OrthancStudyId,
            model,
            modelBytes,
            cancellationToken);

        sw.Stop();

        // Save result to database
        var result = new AIAnalysisJob
        {
            Id = Guid.NewGuid(),
            StudyId = studyId,
            ModelId = model.Id,
            PredictionClass = predictionClass,
            Confidence = confidence,
            Probabilities = JsonSerializer.Serialize(probabilities),
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
            PerformedByUserId = userId,
            PerformedByUserName = userName,
            CreatedAtUtc = DateTime.UtcNow,
            FinishedAtUtc = DateTime.UtcNow,
            Status = "Completed"
        };

        _context.AIAnalysisJobs.Add(result);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "AI analysis completed for study {StudyId}. Prediction class: {ClassIndex} ({Confidence:P2}), Frames used: {FramesUsed}, Time: {Time}ms. Available classes: {ClassNames}",
            studyId, predictionClass, confidence, framesUsed, sw.ElapsedMilliseconds, predictionClassName);

        return result;
    }

    public async Task<List<AIAnalysisJob>> GetStudyAnalysisHistoryAsync(Guid studyId)
    {
        return await _context.AIAnalysisJobs
            .Where(r => r.StudyId == studyId)
            .Include(r => r.Model)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();
    }

    /// <summary>
    /// Execute an AI analysis job (called from Hangfire background worker).
    /// Loads the job record, runs inference, and updates with results/status.
    /// </summary>
    public async Task ExecuteAnalysisJobAsync(Guid analysisJobId, CancellationToken cancellationToken)
    {
        try
        {
            // Load the job record
            var job = await _context.AIAnalysisJobs
                .Include(j => j.Study)
                .Include(j => j.Model)
                .FirstOrDefaultAsync(j => j.Id == analysisJobId, cancellationToken);

            if (job == null)
            {
                _logger.LogError("AI analysis job not found: {JobId}", analysisJobId);
                throw new InvalidOperationException($"Analysis job not found: {analysisJobId}");
            }

            _logger.LogInformation(
                "Executing AI analysis job {JobId} for study {StudyId} with model {ModelId}",
                analysisJobId, job.StudyId, job.ModelId);

            // Update status to Running
            job.Status = "Running";
            job.StartedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            var sw = Stopwatch.StartNew();

            // Get the study
            var study = job.Study ?? await _context.ImagingStudies
                .FirstOrDefaultAsync(s => s.Id == job.StudyId, cancellationToken);
            if (study == null)
                throw new InvalidOperationException($"Study not found: {job.StudyId}");

            if (!study.InstitutionId.HasValue)
                throw new InvalidOperationException($"Study {job.StudyId} has no institution");

            // Get the model
            var model = job.Model ?? await _context.AIModels.FindAsync(new object[] { job.ModelId }, cancellationToken);
            if (model == null)
                throw new InvalidOperationException($"Model {job.ModelId} not found");

            // Use database metadata for logging
            var classNamesJson = model.ClassNames ?? "[]";
            var execClassNames = JsonSerializer.Deserialize<string[]>(classNamesJson) ?? Array.Empty<string>();
            var execPredictionClassName = execClassNames.Length > 0 ? string.Join(", ", execClassNames) : "Unknown";

            var modelBytes = await DownloadModelBytesAsync(model.StorageFileName, cancellationToken);
            var (predictionClass, confidence, probabilities, framesUsed) = await RunStudyInferenceAsync(
                study.InstitutionId.Value,
                study.OrthancStudyId,
                model,
                modelBytes,
                cancellationToken);

            sw.Stop();

            // Update job with results
            job.Status = "Completed";
            job.PredictionClass = predictionClass;
            job.Confidence = confidence;
            job.Probabilities = JsonSerializer.Serialize(probabilities);
            job.ProcessingTimeMs = (int)sw.ElapsedMilliseconds;
            job.FinishedAtUtc = DateTime.UtcNow;
            job.ErrorMessage = null;

            _logger.LogInformation(
                "AI analysis completed for job {JobId}. Prediction class: {ClassIndex} ({Confidence:P2}), Frames used: {FramesUsed}, Time: {Time}ms. Available classes: {ClassNames}",
                analysisJobId, predictionClass, confidence, framesUsed, sw.ElapsedMilliseconds, execPredictionClassName);

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing AI analysis job {JobId}", analysisJobId);

            // Update job with error status
            var job = await _context.AIAnalysisJobs.FirstOrDefaultAsync(
                j => j.Id == analysisJobId, cancellationToken);
            if (job != null)
            {
                job.Status = "Failed";
                job.ErrorMessage = ex.Message;
                job.FinishedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
    }

    private async Task<byte[]> DownloadModelBytesAsync(string storageFileName, CancellationToken cancellationToken)
    {
        using var modelStream = await _modelStorage.DownloadModelAsync(storageFileName, cancellationToken);
        using var buffer = new MemoryStream();
        await modelStream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private async Task<(int predictionClass, decimal confidence, float[] probabilities, int framesUsed)> RunStudyInferenceAsync(
        Guid institutionId,
        string? orthancStudyId,
        AIModel model,
        byte[] modelBytes,
        CancellationToken cancellationToken)
    {
        var selectedFrames = await ExtractRepresentativeFramesFromStudyAsync(
            institutionId,
            orthancStudyId,
            cancellationToken);

        var outputs = new List<InferenceOutput>(selectedFrames.Count);
        foreach (var frameBytes in selectedFrames)
        {
            var input = PreprocessImage(frameBytes, model);
            await using var modelStream = new MemoryStream(modelBytes, writable: false);
            var output = await _inferenceEngine.RunAsync(modelStream, input, model, cancellationToken);
            outputs.Add(output);
        }

        if (outputs.Count == 0)
            throw new InvalidOperationException("No inference outputs were generated from selected DICOM frames.");

        var classCount = outputs[0].Probabilities.Length;
        if (outputs.Any(o => o.Probabilities.Length != classCount))
            throw new InvalidOperationException($"Inconsistent output dimensions across selected frames for model '{model.Name}'.");

        var averagedProbabilities = new float[classCount];
        foreach (var output in outputs)
        {
            for (var index = 0; index < classCount; index++)
                averagedProbabilities[index] += output.Probabilities[index];
        }

        for (var index = 0; index < classCount; index++)
            averagedProbabilities[index] /= outputs.Count;

        var predictionClass = Array.IndexOf(averagedProbabilities, averagedProbabilities.Max());
        var confidence = (decimal)averagedProbabilities[predictionClass];

        return (predictionClass, confidence, averagedProbabilities, outputs.Count);
    }

    private async Task<List<byte[]>> ExtractRepresentativeFramesFromStudyAsync(
        Guid institutionId,
        string? orthancStudyId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orthancStudyId))
            throw new InvalidOperationException("Study has no OrthancStudyId");

        var orthancClient = await _orthancFactory.CreateClientAsync(institutionId);

        try
        {
            // Get study details from Orthanc using its internal ID
            var studyDetails = await orthancClient.GetStudyAsync(orthancStudyId, cancellationToken);
            if (studyDetails == null)
                throw new InvalidOperationException($"Study {orthancStudyId} not found in Orthanc");

            // Download study archive (ZIP) from Orthanc
            _logger.LogInformation("Downloading study archive for {OrthancStudyId}", orthancStudyId);
            using var archiveStream = new MemoryStream();
            await orthancClient.DownloadStudyArchiveAsync(orthancStudyId, archiveStream, cancellationToken);
            archiveStream.Position = 0;

            var selectedFrames = ExtractRepresentativeFramesFromArchive(archiveStream);
            if (selectedFrames.Count == 0)
                throw new InvalidOperationException("No valid DICOM frames could be extracted from study archive.");

            _logger.LogInformation(
                "Selected {FrameCount} representative frames from study {OrthancStudyId} for inference.",
                selectedFrames.Count,
                orthancStudyId);

            return selectedFrames;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract representative frames from study {OrthancStudyId}", orthancStudyId);
            throw;
        }
    }

    private List<byte[]> ExtractRepresentativeFramesFromArchive(MemoryStream archiveStream)
    {
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        var dicomEntries = archive.Entries
            .Where(e => e.Name.EndsWith(".dcm", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxDicomInstancesToInspect)
            .ToList();

        if (dicomEntries.Count == 0)
            return [];

        var candidates = new List<FrameCandidate>();
        foreach (var dicomEntry in dicomEntries)
        {
            using var entryStream = dicomEntry.Open();
            using var memStream = new MemoryStream();
            entryStream.CopyTo(memStream);
            memStream.Position = 0;

            DicomFile? dicomFile = null;
            try
            {
                dicomFile = DicomFile.Open(memStream);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping invalid DICOM entry {EntryName}", dicomEntry.FullName);
            }

            if (dicomFile is null)
                continue;

            DicomPixelData pixelData;
            try
            {
                pixelData = DicomPixelData.Create(dicomFile.Dataset);
            }
            catch
            {
                continue;
            }

            var numberOfFrames = Math.Max(1, pixelData.NumberOfFrames);
            foreach (var frameIndex in SelectFrameIndices(numberOfFrames))
            {
                byte[] imageBytes;
                try
                {
                    imageBytes = ExtractPixelDataFromDicomFrame(dicomFile, frameIndex);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "Skipping frame {FrameIndex} from entry {EntryName} due to extraction error.",
                        frameIndex,
                        dicomEntry.FullName);
                    continue;
                }

                if (imageBytes.Length == 0)
                    continue;

                var score = ScoreFrameQuality(imageBytes);
                candidates.Add(new FrameCandidate(imageBytes, score));
            }
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .Take(MaxTotalFramesForInference)
            .Select(c => c.ImageBytes)
            .ToList();
    }

    private static IEnumerable<int> SelectFrameIndices(int frameCount)
    {
        if (frameCount <= 0)
            yield break;

        if (frameCount == 1)
        {
            yield return 0;
            yield break;
        }

        var indices = new HashSet<int> { 0, frameCount - 1 };
        if (frameCount > 2)
            indices.Add(frameCount / 2);

        foreach (var index in indices.OrderBy(i => i).Take(MaxFramesPerInstance))
            yield return index;
    }

    private double ScoreFrameQuality(byte[] imageBytes)
    {
        using var ms = new MemoryStream(imageBytes);
        using var image = Image.Load<Rgb24>(ms);

        var stepX = Math.Max(1, image.Width / 128);
        var stepY = Math.Max(1, image.Height / 128);
        var values = new List<double>((image.Width / stepX + 1) * (image.Height / stepY + 1));

        for (var y = 0; y < image.Height; y += stepY)
        {
            for (var x = 0; x < image.Width; x += stepX)
            {
                var pixel = image[x, y];
                var luminance = (0.299 * pixel.R) + (0.587 * pixel.G) + (0.114 * pixel.B);
                values.Add(luminance);
            }
        }

        if (values.Count == 0)
            return 0;

        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Count;
        var nonBlackRatio = values.Count(v => v > 8) / (double)values.Count;

        return variance * nonBlackRatio;
    }

    private byte[] ExtractPixelDataFromDicomFrame(DicomFile dicomFile, int frameIndex)
    {
        var dataset = dicomFile.Dataset;

        var pixelData = DicomPixelData.Create(dataset);
        if (pixelData is null)
            throw new InvalidOperationException("No pixel data found in DICOM file");

        var safeFrameIndex = Math.Clamp(frameIndex, 0, Math.Max(0, pixelData.NumberOfFrames - 1));
        var frame = pixelData.GetFrame(safeFrameIndex);
        if (frame == null || frame.Data.Length == 0)
            throw new InvalidOperationException("Failed to extract frame data");

        int rows = dataset.GetSingleValue<int>(DicomTag.Rows);
        int columns = dataset.GetSingleValue<int>(DicomTag.Columns);
        int samplesPerPixel = dataset.GetSingleValue<int>(DicomTag.SamplesPerPixel);
        int bitsAllocated = dataset.GetSingleValue<int>(DicomTag.BitsAllocated);
        int bitsStored = dataset.GetSingleValue<int>(DicomTag.BitsStored);
        int highBit = dataset.GetSingleValue<int>(DicomTag.HighBit);

        _logger.LogInformation(
            "DICOM frame extraction - Rows: {Rows}, Columns: {Columns}, SamplesPerPixel: {Samples}, BitsAllocated: {Bits}, FrameIndex: {FrameIndex}",
            rows, columns, samplesPerPixel, bitsAllocated, safeFrameIndex);

        using var image = new Image<Rgb24>(columns, rows);

        byte shift = (byte)(highBit - bitsStored + 1);
        int pixelIndex = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                byte value = 0;

                if (bitsAllocated == 8 && samplesPerPixel >= 3)
                {
                    if (pixelIndex + 2 < frame.Data.Length)
                    {
                        var r = frame.Data[pixelIndex++];
                        var g = frame.Data[pixelIndex++];
                        var b = frame.Data[pixelIndex++];
                        image[x, y] = new Rgb24(r, g, b);
                        continue;
                    }
                }
                else if (bitsAllocated == 8)
                {
                    if (pixelIndex < frame.Data.Length)
                        value = frame.Data[pixelIndex++];
                }
                else if (bitsAllocated == 16)
                {
                    if (pixelIndex + 1 < frame.Data.Length)
                    {
                        ushort pixelValue = BitConverter.ToUInt16(frame.Data, pixelIndex);
                        pixelIndex += 2;

                        if (bitsStored < 16)
                            pixelValue = (ushort)(pixelValue >> shift);

                        value = (byte)(pixelValue >> 8);
                    }
                }

                var pixel = new Rgb24(value, value, value);
                image[x, y] = pixel;
            }
        }

        using var outputStream = new MemoryStream();
        image.SaveAsPng(outputStream);
        return outputStream.ToArray();
    }

    private InferenceInput PreprocessImage(byte[] imageBytes, AIModel model)
    {
        // Get preprocessing parameters from model metadata
        // Defaults ensure backward compatibility with existing models
        var meanJson = model.PreprocessingMean ?? "[0.485, 0.456, 0.406]";
        var stdJson = model.PreprocessingStd ?? "[0.229, 0.224, 0.225]";
        var imageSize = model.PreprocessingImageSize ?? 512;

        var mean = JsonSerializer.Deserialize<float[]>(meanJson) ?? [0.485f, 0.456f, 0.406f];
        var std = JsonSerializer.Deserialize<float[]>(stdJson) ?? [0.229f, 0.224f, 0.225f];

        if (mean.Length < 3 || std.Length < 3)
            throw new InvalidOperationException($"Invalid preprocessing metadata for model '{model.Name}'. Mean/Std must have 3 channels.");

        using var ms = new MemoryStream(imageBytes);
        using var image = Image.Load<Rgb24>(ms);

        // Resize to model's expected size
        image.Mutate(x => x.Resize(imageSize, imageSize));

        // Convert to tensor [1, 3, imageSize, imageSize]
        var tensorShape = new[] { 1, 3, imageSize, imageSize };
        var tensor = new DenseTensor<float>(tensorShape);

        for (int y = 0; y < imageSize; y++)
        {
            for (int x = 0; x < imageSize; x++)
            {
                var pixel = image[x, y];

                // Normalize each channel [0, 255] -> [0, 1] -> standardize
                tensor[0, 0, y, x] = (pixel.R / 255f - mean[0]) / std[0]; // R
                tensor[0, 1, y, x] = (pixel.G / 255f - mean[1]) / std[1]; // G
                tensor[0, 2, y, x] = (pixel.B / 255f - mean[2]) / std[2]; // B
            }
        }

        return new InferenceInput(tensor.ToArray(), tensorShape);
    }

    private sealed record FrameCandidate(byte[] ImageBytes, double Score);
}
