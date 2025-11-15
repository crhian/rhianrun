using System;
using Discord;

namespace DiscordBot
{
    /// <summary>
    /// Static editor for Discord status messages
    /// </summary>
    public class DiscordStatusStaticEdit
    {
        private string _statusText;
        private string _statusType;
        private string _statusUrl;
        
        public DiscordStatusStaticEdit()
        {
            _statusText = string.Empty;
            _statusType = "Game";
            _statusUrl = string.Empty;
        }
        
        public DiscordStatusStaticEdit(string statusText, string statusType, string statusUrl = "")
        {
            _statusText = statusText;
            _statusType = statusType;
            _statusUrl = statusUrl;
        }
        
        public string StatusText
        {
            get { return _statusText; }
            set { _statusText = value; }
        }
        
        public string StatusType
        {
            get { return _statusType; }
            set { _statusType = value; }
        }
        
        public string StatusUrl
        {
            get { return _statusUrl; }
            set { _statusUrl = value; }
        }
        
        public void SetStatus(string text, string type, string url = "")
        {
            _statusText = text;
            _statusType = type;
            _statusUrl = url;
        }
        
        public void ClearStatus()
        {
            _statusText = string.Empty;
            _statusType = "Game";
            _statusUrl = string.Empty;
        }
        
        public bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(_statusText))
            {
                return false;
            }
            
            if (string.IsNullOrWhiteSpace(_statusType))
            {
                return false;
            }
            
            return true;
        }
        
        public string GetStatusDescription()
        {
            if (!IsValid())
            {
                return "Invalid status";
            }
            
            return $"{_statusType}: {_statusText}";
        }
        
        public Game ToDiscordGame()
        {
            if (!IsValid())
            {
                return null;
            }
            
            ActivityType activityType = GetStatusType();
            
            if (string.IsNullOrWhiteSpace(_statusUrl))
            {
                return new Game(_statusText, activityType);
            }
            else
            {
                return new Game(_statusText, activityType, ActivityFlags.None, _statusUrl);
            }
        }
        
        public void ApplyToClient(Discord.WebSocket.DiscordSocketClient client)
        {
            if (!IsValid())
            {
                throw new InvalidOperationException("Cannot apply invalid status to client");
            }
            
            Game game = ToDiscordGame();
            client.SetGameAsync(game);
        }
        
        public static DiscordStatusStaticEdit Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }
            
            string[] parts = input.Split('|');
            
            if (parts.Length < 2)
            {
                return null;
            }
            
            string statusText = parts[0].Trim();
            string statusType = parts[1].Trim();
            string statusUrl = parts.Length > 2 ? parts[2].Trim() : string.Empty;
            
            return new DiscordStatusStaticEdit(statusText, statusType, statusUrl);
        }
        
        public override string ToString()
        {
            if (string.IsNullOrWhiteSpace(_statusUrl))
            {
                return $"{_statusText}|{_statusType}";
            }
            else
            {
                return $"{_statusText}|{_statusType}|{_statusUrl}";
            }
        }
        
        public bool Equals(DiscordStatusStaticEdit other)
        {
            if (other == null)
            {
                return false;
            }
            
            return _statusText == other._statusText &&
                   _statusType == other._statusType &&
                   _statusUrl == other._statusUrl;
        }
        
        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }
            
            return Equals((DiscordStatusStaticEdit)obj);
        }
        
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (_statusText?.GetHashCode() ?? 0);
                hash = hash * 23 + (_statusType?.GetHashCode() ?? 0);
                hash = hash * 23 + (_statusUrl?.GetHashCode() ?? 0);
                return hash;
            }
        }
        
        public DiscordStatusStaticEdit Clone()
        {
            return new DiscordStatusStaticEdit(_statusText, _statusType, _statusUrl);
        }
        
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(_statusText))
            {
                throw new ArgumentException("Status text cannot be empty", nameof(_statusText));
            }
            
            if (string.IsNullOrWhiteSpace(_statusType))
            {
                throw new ArgumentException("Status type cannot be empty", nameof(_statusType));
            }
            
            string[] validTypes = { "Game", "Stream", "Listen", "Watch" };
            bool isValidType = false;
            
            foreach (string validType in validTypes)
            {
                if (_statusType.Equals(validType, StringComparison.OrdinalIgnoreCase))
                {
                    isValidType = true;
                    break;
                }
            }
            
            if (!isValidType)
            {
                throw new ArgumentException($"Status type must be one of: {string.Join(", ", validTypes)}", nameof(_statusType));
            }
            
            if (_statusType.Equals("Stream", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(_statusUrl))
            {
                throw new ArgumentException("Stream status type requires a URL", nameof(_statusUrl));
            }
        }
        
        public static bool TryParse(string input, out DiscordStatusStaticEdit result)
        {
            result = null;
            
            try
            {
                result = Parse(input);
                return result != null;
            }
            catch
            {
                return false;
            }
        }
        
        public string GetStatusTypeString()
        {
            return _statusType;
        }
        
        public void SetStatusType(string statusType)
        {
            _statusType = statusType;
        }
        
        public void SetStatusType(ActivityType activityType)
        {
            switch (activityType)
            {
                case ActivityType.Playing:
                    _statusType = "Game";
                    break;
                case ActivityType.Streaming:
                    _statusType = "Stream";
                    break;
                case ActivityType.Listening:
                    _statusType = "Listen";
                    break;
                case ActivityType.Watching:
                    _statusType = "Watch";
                    break;
                default:
                    _statusType = "Game";
                    break;
            }
        }
        
        private ActivityType GetStatusType()
        {
            switch (_statusType.ToLowerInvariant())
            {
                case "game":
                case "playing":
                    return ActivityType.Playing;
                case "stream":
                case "streaming":
                    return ActivityType.Streaming;
                case "listen":
                case "listening":
                    return ActivityType.Listening;
                case "watch":
                case "watching":
                    return ActivityType.Watching;
                default:
                    return ActivityType.Playing;
            }
        }
    }
}
