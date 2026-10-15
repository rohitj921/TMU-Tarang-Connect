using System;
using System.Collections.Generic;
using System.Linq;

namespace TMUTarangConnect.Models;

public class AppConfig
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string LoginUrl { get; set; } = "http://172.14.0.1:1000";
    public string TestUrl { get; set; } = "https://www.google.com";
    public int CheckIntervalSeconds { get; set; } = 15;
    public int RetryDelaySeconds { get; set; } = 10;
    public int MaxRetries { get; set; } = 5;
    public bool ShowNotifications { get; set; } = true;
    public bool AutoStartOnBoot { get; set; } = true;
    public string LastLogoutUrl { get; set; } = "";
    public string LastKeepaliveUrl { get; set; } = "";
    public long MaxLogSizeBytes { get; set; } = 1048576; // 1 MB

    // Multi-Account Pool & Auto-Rotation
    public List<UserCredential> Accounts { get; set; } = new();
    public bool AutoRotateOnLimit { get; set; } = true;

    public void EnsureAccounts()
    {
        if (Accounts == null)
        {
            Accounts = new List<UserCredential>();
        }

        // Migrate from legacy single Username/Password if present and accounts list is empty
        if (Accounts.Count == 0 && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password))
        {
            Accounts.Add(new UserCredential
            {
                Label = "Primary Account",
                Username = Username.Trim(),
                Password = Password.Trim(),
                IsActive = true,
                LastStatus = "Standby"
            });
        }

        if (Accounts.Count > 0)
        {
            var active = Accounts.FirstOrDefault(a => a.IsActive);
            if (active == null)
            {
                active = Accounts[0];
                active.IsActive = true;
            }

            Username = active.Username;
            Password = active.Password;
        }
        else
        {
            Username = "";
            Password = "";
        }
    }

    public UserCredential GetActiveAccount()
    {
        EnsureAccounts();
        if (Accounts.Count == 0)
        {
            return new UserCredential
            {
                Label = "No Account",
                Username = "",
                Password = "",
                IsActive = false,
                LastStatus = "Not Configured"
            };
        }
        return Accounts.FirstOrDefault(a => a.IsActive) ?? Accounts[0];
    }

    public bool HasValidCredentials => Accounts != null && Accounts.Any(a => !string.IsNullOrWhiteSpace(a.Username) && !string.IsNullOrWhiteSpace(a.Password));

    public void SetActiveAccount(string idOrUsername)
    {
        EnsureAccounts();
        if (Accounts.Count == 0) return;

        foreach (var acc in Accounts)
        {
            acc.IsActive = (acc.Id == idOrUsername || acc.Username == idOrUsername);
        }

        var active = GetActiveAccount();
        Username = active.Username;
        Password = active.Password;
    }

    public UserCredential? RotateToNextAccount(string failedUsername, string failureReason)
    {
        EnsureAccounts();
        if (Accounts.Count <= 1) return null;

        int currentIndex = Accounts.FindIndex(a => a.Username == failedUsername);
        if (currentIndex >= 0)
        {
            Accounts[currentIndex].LastStatus = failureReason;
        }

        int start = currentIndex >= 0 ? currentIndex : 0;
        for (int i = 1; i < Accounts.Count; i++)
        {
            int nextIndex = (start + i) % Accounts.Count;
            var candidate = Accounts[nextIndex];
            if (!string.IsNullOrWhiteSpace(candidate.Username) && !string.IsNullOrWhiteSpace(candidate.Password))
            {
                foreach (var acc in Accounts) acc.IsActive = false;
                candidate.IsActive = true;
                Username = candidate.Username;
                Password = candidate.Password;
                return candidate;
            }
        }

        return null;
    }
}
