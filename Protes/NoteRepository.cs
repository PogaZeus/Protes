using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace Protes
{
    public interface INoteRepository : IDisposable
    {
        void EnsureSchemaExists();
        List<FullNote> LoadNotes(string searchTerm = "", string searchField = "All");
        void SaveNote(string title, string content, string tags);
        void UpdateNote(long id, string title, string content, string tags);
        void DeleteNote(long id);
    }

    public class SqliteNoteRepository : INoteRepository
    {
        private readonly string _databasePath;

        public SqliteNoteRepository(string databasePath)
        {
            _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
            EnsureAppDataFolder();
        }
        public void Dispose()
        {
            // No-op — connections are opened/closed per method
        }
        private void EnsureAppDataFolder()
        {
            var folder = Path.GetDirectoryName(_databasePath);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }

        public void EnsureSchemaExists()
        {
            using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(@"
                    CREATE TABLE IF NOT EXISTS Notes (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Title TEXT NOT NULL,
                        Content TEXT,
                        Tags TEXT,
                        LastModified TEXT
                    )", conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void SaveNote(string title, string content, string tags)
        {
            using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(
                    "INSERT INTO Notes (Title, Content, Tags, LastModified) VALUES (@title, @content, @tags, @now)", conn))
                {
                    cmd.Parameters.AddWithValue("@title", title ?? "");
                    cmd.Parameters.AddWithValue("@content", content ?? "");
                    cmd.Parameters.AddWithValue("@tags", tags ?? "");
                    cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void UpdateNote(long id, string title, string content, string tags)
        {
            using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(
                    @"UPDATE Notes 
                      SET Title = @title, Content = @content, Tags = @tags, LastModified = @now 
                      WHERE Id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@title", title ?? "");
                    cmd.Parameters.AddWithValue("@content", content ?? "");
                    cmd.Parameters.AddWithValue("@tags", tags ?? "");
                    cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void DeleteNote(long id)
        {
            using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("DELETE FROM Notes WHERE Id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<FullNote> LoadNotes(string searchTerm = "", string searchField = "All")
        {
            var notes = new List<FullNote>();
            string likePattern = EscapeLikePattern(searchTerm);
            string query = GetLoadQuery(searchField, isExternal: false);

            using (var conn = new SQLiteConnection($"Data Source={_databasePath};Version=3;"))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@search", likePattern);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            AppendNote(reader, notes, isExternal: false);
                        }
                    }
                }
            }
            return notes;
        }

        private void AppendNote(System.Data.IDataReader reader, List<FullNote> notes, bool isExternal)
        {
            var id = (long)reader["Id"];
            var title = reader["Title"].ToString();
            var content = reader["Content"].ToString();
            var tags = reader["Tags"].ToString();
            var modified = reader["LastModified"].ToString();

            notes.Add(new FullNote
            {
                Id = id,
                Title = title,
                Content = content,
                Tags = tags,
                LastModified = modified
            });
        }

        private string GetLoadQuery(string searchField, bool isExternal)
        {
            string escapeClause = "ESCAPE '\\'";
            string baseQuery = "SELECT Id, Title, Content, Tags, LastModified FROM Notes";
            if (searchField == "All")
            {
                return $@"{baseQuery}
                          WHERE Title LIKE @search {escapeClause}
                             OR Content LIKE @search {escapeClause}
                             OR Tags LIKE @search {escapeClause}
                          ORDER BY LastModified DESC";
            }
            else
            {
                string col = GetColumnName(searchField);
                return $@"{baseQuery}
                          WHERE {col} LIKE @search {escapeClause}
                          ORDER BY LastModified DESC";
            }
        }

        private string EscapeLikePattern(string input)
        {
            if (string.IsNullOrEmpty(input)) return "%";
            return "%" + input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        }

        private string GetColumnName(string searchField)
        {
            switch (searchField)
            {
                case "Title":
                    return "Title";
                case "Content":
                    return "Content";
                case "Tags":
                    return "Tags";
                case "Modified":
                    return "LastModified";
                default:
                    return "Title";
            }
        }
    }

    public class MySqlNoteRepository : INoteRepository
    {
        private readonly string _connectionString;

        public MySqlNoteRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public void EnsureSchemaExists()
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SHOW TABLES LIKE 'Notes';", conn))
                {
                    if (cmd.ExecuteScalar() == null)
                        throw new InvalidOperationException("Table 'Notes' not found in the external database.");
                }
            }
        }
        public void Dispose()
        {
            // No-op — connections are opened/closed per method
        }
        public void SaveNote(string title, string content, string tags)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(
                    "INSERT INTO Notes (Title, Content, Tags, LastModified) VALUES (@title, @content, @tags, @now)", conn))
                {
                    cmd.Parameters.AddWithValue("@title", title ?? "");
                    cmd.Parameters.AddWithValue("@content", content ?? "");
                    cmd.Parameters.AddWithValue("@tags", tags ?? "");
                    cmd.Parameters.AddWithValue("@now", DateTime.Now);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void UpdateNote(long id, string title, string content, string tags)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(
                    @"UPDATE Notes 
                      SET Title = @title, Content = @content, Tags = @tags, LastModified = @now 
                      WHERE Id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@title", title ?? "");
                    cmd.Parameters.AddWithValue("@content", content ?? "");
                    cmd.Parameters.AddWithValue("@tags", tags ?? "");
                    cmd.Parameters.AddWithValue("@now", DateTime.Now);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void DeleteNote(long id)
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("DELETE FROM Notes WHERE Id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<FullNote> LoadNotes(string searchTerm = "", string searchField = "All")
        {
            var notes = new List<FullNote>();
            string likePattern = EscapeLikePattern(searchTerm);
            string query = GetLoadQuery(searchField, isExternal: true);

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@search", likePattern);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            AppendNote(reader, notes, isExternal: true);
                        }
                    }
                }
            }
            return notes;
        }

        private void AppendNote(System.Data.IDataReader reader, List<FullNote> notes, bool isExternal)
        {
            var id = Convert.ToInt64(reader["Id"]);
            var title = reader["Title"].ToString();
            var content = reader["Content"].ToString();
            var tags = reader["Tags"].ToString();
            string modified = reader["LastModified"] is DateTime dt
                ? dt.ToString("yyyy-MM-dd HH:mm")
                : reader["LastModified"].ToString();

            notes.Add(new FullNote
            {
                Id = id,
                Title = title,
                Content = content,
                Tags = tags,
                LastModified = modified
            });
        }

        private string GetLoadQuery(string searchField, bool isExternal)
        {
            string escapeClause = "ESCAPE '\\\\'";
            string baseQuery = "SELECT Id, Title, Content, Tags, LastModified FROM Notes";
            if (searchField == "All")
            {
                return $@"{baseQuery}
                          WHERE Title LIKE @search {escapeClause}
                             OR Content LIKE @search {escapeClause}
                             OR Tags LIKE @search {escapeClause}
                          ORDER BY LastModified DESC";
            }
            else
            {
                string col = GetColumnName(searchField);
                return $@"{baseQuery}
                          WHERE {col} LIKE @search {escapeClause}
                          ORDER BY LastModified DESC";
            }
        }

        private string EscapeLikePattern(string input)
        {
            if (string.IsNullOrEmpty(input)) return "%";
            return "%" + input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        }

        private string GetColumnName(string searchField)
        {
            switch (searchField)
            {
                case "Title":
                    return "Title";
                case "Content":
                    return "Content";
                case "Tags":
                    return "Tags";
                case "Modified":
                    return "LastModified";
                default:
                    return "Title";
            }
        }
    }
}