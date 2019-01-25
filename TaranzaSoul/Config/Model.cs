using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace TaranzaSoul
{
    public class UserLogContext : DbContext
    {
        public DbSet<Users> UserList { get; set; }
        public DbSet<WatchedUsers> WatchedUserList { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=users.db");
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Users>()
                .HasKey(x => x.UserId);

            builder.Entity<WatchedUsers>()
                .HasKey(x => x.UserId);
        }
    }

    public class Users
    {
        public ulong UserId { get; set; }
        public bool ApprovedAccess { get; set; }
        public bool NewAccount { get; set; }
        public ulong ApprovalModId { get; set; }
        public string ApprovalReason { get; set; }
    }

    public class WatchedUsers
    {
        public ulong UserId { get; set; }
        public string Reason { get; set; }
    }
}
