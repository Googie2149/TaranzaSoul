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
        private string FCConnectionString;
        private bool FCDBInitialized = false;
        private bool MessageListInitialized = false;

        public async Task Install(IServiceProvider _services)
        {
            ConnectionString = _services.GetService<Config>().DatabaseConnectionString;
            FCConnectionString = _services.GetService<Config>().FriendCodeDatabaseConnectionString;
        }

        #region FriendCodes

        public async Task InitializedFCDB()
        {
            if (FCDBInitialized)
                return;

            using (SQLiteConnection db = new SQLiteConnection(FCConnectionString))
            {
                await db.OpenAsync();

                using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='switchfcs';", db))
                {
                    if ((await cmd.ExecuteScalarAsync()) != null)
                        FCDBInitialized = true;
                }

                using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='switchfcs';", db))
                {
                    if ((await cmd.ExecuteScalarAsync()) != null)
                        FCDBInitialized = true;
                }

                if (!FCDBInitialized)
                {
                    using (var cmd = new SQLiteCommand("CREATE TABLE IF NOT EXISTS switchfcs " +
                        "(UserId TEXT NOT NULL PRIMARY KEY, FriendCode TEXT, SwitchNickname TEXT, MessageId TEXT);", db))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                db.Close();
            }
        }

        public async Task<SwitchUser> GetSwitchFC(ulong userId)
        {
            SwitchUser temp = null;

            using (SQLiteConnection db = new SQLiteConnection(FCConnectionString))
            {
                await db.OpenAsync();

                using (var cmd = new SQLiteCommand("select * from switchfcs where UserId = @1;", db))
                {
                    cmd.Parameters.AddWithValue("@1", userId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            temp = new SwitchUser()
                            {
                                UserId = Convert.ToUInt64((string)reader["UserId"]),
                                FriendCode = (reader["FriendCode"] == DBNull.Value) ? 0 : Convert.ToUInt64((string)reader["FriendCode"]),
                                SwitchNickname = reader["SwitchNickname"] == DBNull.Value ? null : (string)reader["SwitchNickname"],
                                MessageId = (reader["MessageId"] == DBNull.Value) ? 0 : Convert.ToUInt64((string)reader["MessageId"])
                            };
                        }
                    }
                }

                db.Close();
            }

            return temp;
        }

        public async Task<Dictionary<ulong, SwitchUser>> GetAllFriendCodes()
        {
            Dictionary<ulong, SwitchUser> temp = new Dictionary<ulong, SwitchUser>();

            try
            {
                using (SQLiteConnection db = new SQLiteConnection(FCConnectionString))
                {
                    await db.OpenAsync();

                    using (var cmd = new SQLiteCommand("select * from switchfcs;", db))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                temp.Add(Convert.ToUInt64((string)reader["UserId"]),
                                    new SwitchUser()
                                    {
                                        UserId = Convert.ToUInt64((string)reader["UserId"]),
                                        FriendCode = (reader["FriendCode"] == DBNull.Value) ? 0 : Convert.ToUInt64((string)reader["FriendCode"]),
                                        SwitchNickname = reader["SwitchNickname"] == DBNull.Value ? null : (string)reader["SwitchNickname"],
                                        MessageId = (reader["MessageId"] == DBNull.Value) ? 0 : Convert.ToUInt64((string)reader["MessageId"])
                                    });
                            }
                        }
                    }

                    db.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EError getting all FCs!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                //System.Environment.Exit(0);
            }

            return temp;
        }

        public async Task<SwitchUser> AddFriendCode(ulong userId, ulong friendCode, ulong messageId, string switchNickname = null)
        {
            SwitchUser temp = new SwitchUser() { UserId = userId, FriendCode = friendCode, SwitchNickname = switchNickname, MessageId = messageId };

            await BulkAddFriendCodes(new List<SwitchUser> { temp });

            return temp;
        }

        public async Task RemoveFriendCode(ulong userId)
        {
            try
            {
                using (SQLiteConnection db = new SQLiteConnection(FCConnectionString))
                {
                    await db.OpenAsync();

                    using (var cmd = new SQLiteCommand("delete from switchfcs where UserId = @1;", db))
                    {
                        cmd.Parameters.AddWithValue("@1", userId.ToString());

                        await cmd.ExecuteNonQueryAsync();
                    }

                    db.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting a friend code!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
            }
        }

        public async Task BulkAddFriendCodes(IEnumerable<SwitchUser> users)
        {
            try
            {
                using (SQLiteConnection db = new SQLiteConnection(FCConnectionString))
                {
                    await db.OpenAsync();
                    using (var tr = db.BeginTransaction())
                    {
                        foreach (var u in users)
                        {
                            using (var cmd = new SQLiteCommand("insert into switchfcs (UserId, FriendCode, SwitchNickname, MessageId) values (@1, @2, @3, @4);", db))
                            {
                                cmd.Parameters.AddWithValue("@1", u.UserId.ToString());
                                cmd.Parameters.AddWithValue("@2", u.FriendCode.ToString() ?? null);
                                cmd.Parameters.AddWithValue("@3", u.SwitchNickname ?? null);
                                cmd.Parameters.AddWithValue("@4", u.MessageId.ToString() ?? null);

                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        tr.Commit();
                    }

                    db.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error bulk adding FCs!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
            }
        }

        public async Task EditFriendCode(ulong userId, ulong friendCode, ulong messageId, string switchNickname = null)
        {
            using (SQLiteConnection db = new SQLiteConnection(FCConnectionString))
            {
                await db.OpenAsync();

                using (var cmd = new SQLiteCommand("update switchfcs set (FriendCode, SwitchNickname, MessageId) = (@2, @3, @4) where UserId = @1;", db))
                {
                    cmd.Parameters.AddWithValue("@1", userId.ToString());
                    cmd.Parameters.AddWithValue("@2", friendCode.ToString() ?? null);
                    cmd.Parameters.AddWithValue("@3", switchNickname ?? null);
                    cmd.Parameters.AddWithValue("@4", messageId.ToString() ?? null);

                    await cmd.ExecuteNonQueryAsync();
                }

                db.Close();
            }
        }

        #endregion

        #region UserLogging
        public async Task<bool> InitializeDB()
        {
            bool tableExists = false;

            using (SQLiteConnection db = new SQLiteConnection(ConnectionString))
            {
                await db.OpenAsync();

                using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='users';", db))
                {
                    if ((await cmd.ExecuteScalarAsync()) != null)
                        tableExists = true;
                }

                if (!tableExists)
                {
                    using (var cmd = new SQLiteCommand("CREATE TABLE IF NOT EXISTS users " +
                        "(UserId TEXT NOT NULL PRIMARY KEY, ApprovedAccess INTEGER NOT NULL, NewAccount INTEGER NOT NULL, ApprovalModId TEXT, ApprovalReason TEXT);", db))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                db.Close();
            }

            return tableExists;
        }

        public async Task<LoggedUser> GetLoggedUser(ulong userId)
        {
            LoggedUser temp = null;

            using (SQLiteConnection db = new SQLiteConnection(ConnectionString))
            {
                await db.OpenAsync();

                using (var cmd = new SQLiteCommand("select * from users where UserId = @1;", db))
                {
                    cmd.Parameters.AddWithValue("@1", userId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            temp = new LoggedUser()
                            {
                                UserId = Convert.ToUInt64((string)reader["UserId"]),
                                ApprovedAccess = (long)reader["ApprovedAccess"] == 1 ? true : false,
                                NewAccount = (long)reader["NewAccount"] == 1 ? true : false,
                                ApprovalModId = reader["ApprovalModId"] == DBNull.Value ? 0 : Convert.ToUInt64((string)reader["ApprovalModId"]),
                                ApprovalReason = reader["ApprovalReason"] == DBNull.Value ? null : (string)reader["ApprovalReason"]
                            };
                        }
                    }
                }

                db.Close();
            }

            return temp;
        }

        public async Task<Dictionary<ulong, LoggedUser>> GetAllusers()
        {
            Dictionary<ulong, LoggedUser> temp = new Dictionary<ulong, LoggedUser>();

            try
            {
                using (SQLiteConnection db = new SQLiteConnection(ConnectionString))
                {
                    await db.OpenAsync();

                    using (var cmd = new SQLiteCommand("select * from users;", db))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                temp.Add(Convert.ToUInt64((string)reader["UserId"]),
                                    new LoggedUser()
                                    {
                                        UserId = Convert.ToUInt64((string)reader["UserId"]),
                                        ApprovedAccess = ((long)reader["ApprovedAccess"] == 1) ? true : false,
                                        NewAccount = ((long)reader["NewAccount"] == 1) ? true : false,
                                        ApprovalModId = (reader["ApprovalModId"] == DBNull.Value) ? 0 : Convert.ToUInt64((string)reader["ApprovalModId"]),
                                        ApprovalReason = (reader["ApprovalReason"] == DBNull.Value) ? null : (string)reader["ApprovalReason"]
                                    });
                            }
                        }
                    }

                    db.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error bulk saving!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                //System.Environment.Exit(0);
            }

            return temp;
        }

        public async Task<LoggedUser> AddLoggedUser(ulong userId, bool approvedAccess = false, bool newAccount = false)
        {
            LoggedUser temp = new LoggedUser() { UserId = userId, ApprovedAccess = approvedAccess, NewAccount = newAccount };

            await BulkAddLoggedUser(new List<LoggedUser> { temp });

            return temp;
        }

        public async Task BulkAddLoggedUser(IEnumerable<LoggedUser> users)
        {
            try
            {
                using (SQLiteConnection db = new SQLiteConnection(ConnectionString))
                {
                    await db.OpenAsync();
                    using (var tr = db.BeginTransaction())
                    {
                        foreach (var u in users)
                        {
                            using (var cmd = new SQLiteCommand("insert into users (UserId, ApprovedAccess, NewAccount, ApprovalModId, ApprovalReason) values (@1, @2, @3, @4, @5);", db))
                            {
                                cmd.Parameters.AddWithValue("@1", u.UserId.ToString());
                                cmd.Parameters.AddWithValue("@2", u.ApprovedAccess ? 1 : 0); // to the me from the future: this is converting true/false into 1/0
                                cmd.Parameters.AddWithValue("@3", u.NewAccount ? 1 : 0);
                                cmd.Parameters.AddWithValue("@4", u.ApprovalModId.ToString() ?? null);
                                cmd.Parameters.AddWithValue("@5", u.ApprovalReason ?? null);

                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        tr.Commit();
                    }

                    db.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error bulk saving!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
            }
        }

        public async Task AutoApproveUser(ulong userId)
        {
            using (SQLiteConnection db = new SQLiteConnection(ConnectionString))
            {
                await db.OpenAsync();

                using (var cmd = new SQLiteCommand("update users set ApprovedAccess = @1 where UserId = @2;", db))
                {
                    cmd.Parameters.AddWithValue("@1", 1);
                    cmd.Parameters.AddWithValue("@2", userId.ToString());

                    await cmd.ExecuteNonQueryAsync();
                }

                db.Close();
            }
        }

        public async Task ModApproveUser(ulong userId, ulong modId, string reason)
        {
            using (SQLiteConnection db = new SQLiteConnection(ConnectionString))
            {
                await db.OpenAsync();

                using (var cmd = new SQLiteCommand("update users set (ApprovedAccess, ApprovalModId, ApprovalReason) = (@1, @2, @3) where UserId = @4;", db))
                {
                    cmd.Parameters.AddWithValue("@1", 1);
                    cmd.Parameters.AddWithValue("@2", modId.ToString());
                    cmd.Parameters.AddWithValue("@3", reason);
                    cmd.Parameters.AddWithValue("@4", userId.ToString());

                    await cmd.ExecuteNonQueryAsync();
                }

                db.Close();
            }
        }

        public async Task RevokeApproval(ulong userId)
        {
            using (SQLiteConnection db = new SQLiteConnection(ConnectionString))
            {
                await db.OpenAsync();

                using (var cmd = new SQLiteCommand("update users set ApprovedAccess = @1 where UserId = @2;", db))
                {
                    cmd.Parameters.AddWithValue("@1", 0);
                    cmd.Parameters.AddWithValue("@2", userId.ToString());

                    await cmd.ExecuteNonQueryAsync();
                }

                db.Close();
            }
        }
        #endregion
    }

    public class LoggedUser
    {
        public ulong UserId { get; set; }
        public bool ApprovedAccess { get; set; }
        public bool NewAccount { get; set; }
        public ulong ApprovalModId { get; set; }
        public string ApprovalReason { get; set; }
    }

    public class SwitchUser
    {
        public ulong UserId { get; set; }
        public ulong FriendCode { get; set; }
        public string SwitchNickname { get; set; }
        public ulong MessageId { get; set; }
    }

    //public class ChannelPins
    //{
    //    public ulong ChannelId { get; set; }
    //    public List<ulong> MessageIds { get; set; }
    //}
}
