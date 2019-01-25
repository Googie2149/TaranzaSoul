using System;
using System.Collections.Generic;
using System.Text;
using System.Data.SQLite;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TaranzaSoul
{
    public class DatabaseHelper
    {
        private string ConnectionString;

        public async Task Install(IServiceProvider _services)
        {
            ConnectionString = _services.GetService<Config>().DatabaseConnectionString;
        }

        public async Task<LoggedUser> GetUser(ulong userId, string dbstring)
        {
            LoggedUser temp = null;

            using (SQLiteConnection db = new SQLiteConnection(dbstring))
            {
                await db.OpenAsync();

                using (var cmd = new SQLiteCommand($"select * from users where UserId = @1", db))
                {
                    cmd.Parameters.AddWithValue("@1", userId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            temp = new LoggedUser()
                            {
                                UserId = (ulong)reader["UserId"],
                                ApprovedAccess = (bool)reader["ApprovedAccess"],
                                NewAccount = (bool)reader["NewAccount"],
                                ApprovalModId = (ulong)reader["ApprovalModId"],
                                ApprovalReason = (string)reader["ApprovalReason"]
                            };
                        }
                    }
                }
            }

                return null;
        }
    }

    public class LoggedUser
    {
        public ulong UserId { get; set; }
        public bool ApprovedAccess { get; set; }
        public bool NewAccount { get; set; }
        public ulong ApprovalModId { get; set; }
        public string ApprovalReason { get; set; }
    }
}
