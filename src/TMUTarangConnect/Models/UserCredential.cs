using System;

namespace TMUTarangConnect.Models;

public class UserCredential
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "Primary Account";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool IsActive { get; set; } = false;
    public string LastStatus { get; set; } = "Standby"; // "Active", "Over Limit", "Invalid", "Standby"

    public string DisplayTitle => string.IsNullOrWhiteSpace(Label) ? Username : $"{Label} ({Username})";

    public string SwitchButtonText => IsActive ? "Active" : "Switch";
    public bool CanSwitch => !IsActive;
    public string CardBorderBrush => IsActive ? "#3B82F6" : "#1C2333";
    public string CardBackground => IsActive ? "#101827" : "#0E111A";

    public string StatusBadgeBg => LastStatus switch
    {
        "Active" => "#064E3B",
        "Over Limit" => "#7F1D1D",
        "Invalid" => "#831843",
        _ => "#1E293B"
    };

    public string StatusBadgeColor => LastStatus switch
    {
        "Active" => "#34D399",
        "Over Limit" => "#F87171",
        "Invalid" => "#F472B6",
        _ => "#94A3B8"
    };
}
