namespace AntColonyServer
{
    /// <summary>
    /// POCO класс для JSON
    /// Хранит поля представления начальной конфигурации сервера
    /// </summary>
    public class ServerConfig
    {
        public int NumClients { get; set; }
        public int MaxAnts { get; set; }
        public int InPort { get; set; }
        public int MaxIteration { get; set; }
        public double Alpha { get; set; }
        public double Beta { get; set; }
        public int Q { get; set; }
        public double RHO { get; set; }
        public int CountSubjects { get; set; }

        /// <summary>
        /// Значение по умолчанию для сервера
        /// </summary>
        public ServerConfig()
        {
            NumClients = 2;
            MaxAnts = 20;
            InPort = 8081;
            MaxIteration = 100;
            Alpha = 1.0;
            Beta = 5.0;
            Q = 100;
            RHO = 0.1;
            CountSubjects = 1000;
        }
    }
}