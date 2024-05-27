using DBCD.Providers;

namespace DBXPatchTool.Infrastructure
{
    public class DBCProvider : IDBCProvider
    {
        public Stream StreamForTableName(string tableName, string build) => File.OpenRead(tableName);
    }
}
