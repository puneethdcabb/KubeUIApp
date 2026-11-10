using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using KubeUIApp.Models;

namespace KubeUIApp.Services
{
    public class ProfileStorageService
    {
        private readonly string _filePath;

        public ProfileStorageService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "KubeUIApp");
            Directory.CreateDirectory(appDir);
            _filePath = Path.Combine(appDir, "profiles.json");
        }

        public List<ConnectionProfile> LoadProfiles()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var profiles = JsonSerializer.Deserialize<List<ConnectionProfile>>(json);
                    if (profiles != null && profiles.Count > 0)
                    {
                        return profiles;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ProfileStorageService.LoadProfiles", $"Failed to load profiles from {_filePath}.", ex);
            }

            return new List<ConnectionProfile>
            {
                new ConnectionProfile
                {
                    ProfileName = "Local MicroShift / OpenShift Node",
                    Host = "192.168.1.100",
                    Port = 22,
                    Username = "root",
                    Password = "",
                    PreferredCli = "Auto",
                    PreferredContainerRuntime = "Auto"
                }
            };
        }

        public void SaveProfiles(List<ConnectionProfile> profiles)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(profiles, options);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ProfileStorageService.SaveProfiles", $"Failed to save profiles to {_filePath}.", ex);
            }
        }
    }
}
