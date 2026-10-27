using System;
using Npgsql;

namespace GIS.PostGIS
{
    /// <summary>
    /// 管理 PostgreSQL/PostGIS 连接，并提供基础连接测试。
    /// </summary>
    public sealed class PostGISConnection
    {
        private readonly string _connectionString;

        public PostGISConnection(
            string host,
            int port,
            string database,
            string username,
            string password)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("数据库地址不能为空。", nameof(host));
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "端口号必须在 1 到 65535 之间。");
            if (string.IsNullOrWhiteSpace(database))
                throw new ArgumentException("数据库名称不能为空。", nameof(database));
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("用户名不能为空。", nameof(username));

            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = database,
                Username = username,
                Password = password ?? string.Empty,
                Timeout = 5,
                CommandTimeout = 10
            };

            _connectionString = builder.ConnectionString;
        }

        /// <summary>
        /// 打开连接并返回数据库中的 PostGIS 版本。
        /// </summary>
        public string TestConnection()
        {
            using (NpgsqlConnection connection = CreateConnection())
            {
                connection.Open();
                using (NpgsqlCommand command = new NpgsqlCommand("SELECT PostGIS_Version();", connection))
                {
                    object result = command.ExecuteScalar();
                    return Convert.ToString(result);
                }
            }
        }

        /// <summary>
        /// 创建一个尚未打开的数据库连接，由调用方负责释放。
        /// </summary>
        public NpgsqlConnection CreateConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }
    }
}