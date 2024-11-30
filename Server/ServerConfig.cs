namespace AntColonyServer
{
    public class ServerConfig
    {
        public string[] NameClients { get; set; }
        public int MaxAnts { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int InPort { get; set; }
        public int OutPort { get; set; }
        public int maxIteration { get; set; }
        public double Alpha { get; set; }
        public double Beta { get; set; }
        public int Q { get; set; }
        public double RHO { get; set; }
        public int CountSubjects { get; set; }
        public string PathToEXE { get; set; }
        public string NameFile { get; set; }

        public bool LocalTest { get; set; }
        public bool UploadFile { get; set; }
        public string ProtocolType { get; set; }

        public ServerConfig()
        {
            NameClients ??= new string[0];
            MaxAnts = 20;
            Username = "";
            Password = "";
            InPort = 7080;
            OutPort = 9090;
            maxIteration = 100;
            Alpha = 1.0;
            Beta = 5.0;
            Q = 100;
            RHO = 0.1;
            CountSubjects = 1000;
            PathToEXE = "C:\\temp";
            NameFile = "Client.exe";
            LocalTest = true;
            UploadFile = false;
            ProtocolType = "tcp";
        }
    }
}
