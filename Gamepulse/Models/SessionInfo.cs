using System;

namespace Gamepulse.Models
{
    public class SessionInfo
    {
        public string SessionId { get; }
        public string GameName { get; }
        public DateTime StartTime { get; }

        public SessionInfo(string sessionId, string gameName, DateTime startTime)
        {
            SessionId = sessionId;
            GameName = gameName;
            StartTime = startTime;
        }
    }
}