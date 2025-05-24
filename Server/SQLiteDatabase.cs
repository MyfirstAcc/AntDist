using System.Data.SQLite;

namespace AntColonyServer
{
    /// <summary>
    /// Класс для работы с БД SQLite
    /// Сохраняет результаты прогона алгоритма 
    /// </summary>
    internal class SQLiteDatabase
    {
        private readonly string _connectionString;

        public SQLiteDatabase(string dbFilePath)
        {
            _connectionString = $"Data Source={dbFilePath};Version=3;";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            if (!File.Exists("testsAnts.db"))
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();

                    string createTestRunsTable = @"
                    CREATE TABLE IF NOT EXISTS TestRuns (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TestType TEXT NOT NULL,
                        Data DATETIME NOT NULL,
                        Local INTEGER,
                        TypeProtocol TEXT NOT NULL
                    );";

                    string createTestParametersTable = @"
                    CREATE TABLE IF NOT EXISTS TestParameters (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TestRunId INTEGER NOT NULL,
                        ParameterName TEXT NOT NULL,
                        ParameterValue TEXT NOT NULL,
                        FOREIGN KEY (TestRunId) REFERENCES TestRuns(Id)
                    );";

                    string createTestResultsTable = @"
                    CREATE TABLE IF NOT EXISTS TestResults (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TestRunId INTEGER NOT NULL,
                        BestItems TEXT NOT NULL,
                        BestValue REAL NOT NULL,
                        MethodRunTime REAL NOT NULL,
                        TotalRunTime REAL NOT NULL,
                        FOREIGN KEY (TestRunId) REFERENCES TestRuns(Id)
                    );";

                    using (var command = new SQLiteCommand(createTestRunsTable, connection))
                        command.ExecuteNonQuery();
                    using (var command = new SQLiteCommand(createTestParametersTable, connection))
                        command.ExecuteNonQuery();
                    using (var command = new SQLiteCommand(createTestResultsTable, connection))
                        command.ExecuteNonQuery();
                }
            }
        }

        public int AddTestRun(string testType, DateTime startTime, bool local, string typeProtocol)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string insertTestRun = @"
                INSERT INTO TestRuns (TestType, Data, Local, TypeProtocol)
                VALUES (@TestType, @Data, @Local, @TypeProtocol);
                SELECT last_insert_rowid();";

                using (var command = new SQLiteCommand(insertTestRun, connection))
                {
                    command.Parameters.AddWithValue("@TestType", testType);
                    command.Parameters.AddWithValue("@Data", startTime);
                    command.Parameters.AddWithValue("@Local", Convert.ToInt32(local));
                    command.Parameters.AddWithValue("@TypeProtocol", typeProtocol);
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        public void AddTestParameter(int testRunId, string parameterName, string parameterValue)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string insertParameter = @"
                INSERT INTO TestParameters (TestRunId, ParameterName, ParameterValue)
                VALUES (@TestRunId, @ParameterName, @ParameterValue);";

                using (var command = new SQLiteCommand(insertParameter, connection))
                {
                    command.Parameters.AddWithValue("@TestRunId", testRunId);
                    command.Parameters.AddWithValue("@ParameterName", parameterName);
                    command.Parameters.AddWithValue("@ParameterValue", parameterValue);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void AddTestResult(int testRunId, string bestItems, double bestValue, double methodRunTime, double clientStartTime)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string insertResult = @"
                INSERT INTO TestResults (TestRunId, BestItems, BestValue, MethodRunTime, TotalRunTime)
                VALUES (@TestRunId, @BestItems, @BestValue, @MethodRunTime, @TotalRunTime);";

                using (var command = new SQLiteCommand(insertResult, connection))
                {
                    command.Parameters.AddWithValue("@TestRunId", testRunId);
                    command.Parameters.AddWithValue("@BestItems", bestItems);
                    command.Parameters.AddWithValue("@BestValue", bestValue);
                    command.Parameters.AddWithValue("@MethodRunTime", methodRunTime);
                    command.Parameters.AddWithValue("@TotalRunTime", clientStartTime);
                    command.ExecuteNonQuery();
                }
            }
        }

        public List<TestResult> GetTestResults()
        {
            var results = new List<TestResult>();
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = @"
                SELECT tr.Id, tr.TestType, tr.Data, res.BestItems, res.BestValue, res.MethodRunTime, res.TotalRunTime
                FROM TestRuns tr
                JOIN TestResults res ON tr.Id = res.TestRunId";
                using (var command = new SQLiteCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(new TestResult
                            {
                                TestRunId = reader.GetInt32(0),
                                TestType = reader.GetString(1),
                                Data = reader.GetDateTime(2),
                                BestItems = reader.GetString(3),
                                BestValue = reader.GetDouble(4),
                                MethodRunTime = reader.GetDouble(5),
                                TotalRunTime = reader.GetDouble(6)
                            });
                        }
                    }
                }
            }
            return results;
        }
        public async Task<List<object>> GetChartDataAsync()
        {
            var chartData = new List<object>();
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT
                        GROUP_CONCAT(
                            CASE 
                                WHEN TestParameters.ParameterName = 'NumClients' THEN TestParameters.ParameterValue
                                ELSE NULL 
                            END, ', '
                        ) AS NumClients,
                        TestResults.BestValue,
                        TestResults.MethodRunTime,
                        TestResults.TotalRunTime - TestResults.MethodRunTime As StartTimeClient  
                    FROM 
                        TestRuns
                    JOIN 
                        TestParameters ON TestRuns.Id = TestParameters.TestRunId
                    JOIN
                        TestResults ON TestRuns.Id = TestResults.TestRunId
                    GROUP BY 
                        TestRuns.Id, TestResults.BestValue, TestResults.MethodRunTime;
                ";

                using (var command = new SQLiteCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            chartData.Add(new
                            {
                                NumClients = reader["NumClients"].ToString().Split(", ").Select(int.Parse).ToList(),
                                BestValue = reader.GetDouble(reader.GetOrdinal("BestValue")),
                                MethodRunTime = reader.GetDouble(reader.GetOrdinal("MethodRunTime")),
                                StartTimeClient = reader.GetDouble(reader.GetOrdinal("StartTimeClient"))
                            });
                        }
                    }
                }
            }
            return chartData;
        }
    }
    public class TestResult
    {
        public int TestRunId { get; set; }
        public string TestType { get; set; }
        public DateTime Data { get; set; }
        public string BestItems { get; set; }
        public double BestValue { get; set; }
        public double MethodRunTime { get; set; }
        public double TotalRunTime { get; set; }
    }
}