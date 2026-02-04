using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sitim.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // Pentru pasul 2 poți începe fără tabele proprii,
        // sau definești minimal: Analyses, Reports (mai târziu).
    }
}
