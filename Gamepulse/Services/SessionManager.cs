using System;
using Gamepulse.Models;

namespace Gamepulse.Services
{
    public class SessionManager
    {
        public bool IsRunning { get; private set; }
        public SessionInfo? CurrentSession { get; private set; }

        public void StartSession(string gameName)
        {
            string sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            CurrentSession = new SessionInfo(
                sessionId,
                string.IsNullOrWhiteSpace(gameName) ? "Unknown" : gameName,
                DateTime.Now
            );

            IsRunning = true;
        }

        public void StopSession()
        {
            IsRunning = false;
        }

        public int GetElapsedSeconds()
        {
            if (CurrentSession == null)
            {
                return 0;
            }

            return (int)(DateTime.Now - CurrentSession.StartTime).TotalSeconds;
        }

        public TimeSpan GetDuration()
        {
            if (CurrentSession == null)
            {
                return TimeSpan.Zero;
            }

            return DateTime.Now - CurrentSession.StartTime;
        }
    }
}