# TMU Tarang Connect ⚡

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/rohitj921)
[![Framework](https://img.shields.io/badge/.NET-9.0%20WPF-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-10B981?style=for-the-badge)](LICENSE)
[![Release](https://img.shields.io/badge/Release-v2.4%20Stable-38BDF8?style=for-the-badge)](https://github.com/rohitj921)

A high-performance, native Windows 11 desktop application and system tray automation engine for **Teerthanker Mahaveer University (TMU)** Wi-Fi networks (FortiGate Captive Portal).

---

## 🎯 Why TMU Tarang Connect? (The Problem It Solves)

Whether you are a university employee, faculty member, researcher, or student, anyone working on the campus network at Teerthanker Mahaveer University (TMU) experiences the same daily friction:

- **Repeated Manual Logins:** Having to open a browser window and re-enter your credentials every time you boot your laptop, wake from sleep, or move between campus offices and buildings.
- **Abrupt Session Disconnections:** Network sessions suddenly dropping during daily workflow, development tasks, administrative duties, academic research, portal submissions, or heavy downloads.
- **Strict 2-Device Limit per ID:** Every employee and student receives exactly 1 login tied to their registered mobile number, restricted to 2 active devices simultaneously. If your smartphone and tablet are connected, your work laptop is locked out with *"Maximum login limit reached"*.
- **Zero Real-Time Visibility:** No native way to monitor gateway ping latency, internet speed, or daily bandwidth consumption.

### The Solution:
**TMU Tarang Connect** runs quietly in your Windows system tray and takes care of network friction automatically:
- ⚡ **Instant Background Login:** Detects campus Wi-Fi or Ethernet instantly and authenticates you in under a second.
- 🔄 **Standby Account Rotation:** If your primary login is occupied by your phone or other device, the app automatically switches to a standby login (such as a colleague, teammate, or roommate whose quota is available) so your work continues uninterrupted.
- 🛡️ **Continuous Connection Persistence:** Actively monitors your internet route. If the session times out or disconnects, it re-authenticates automatically without you touching a thing.
- 📊 **All-in-One Dashboard:** Built-in network speed benchmark, daily and monthly data counters, live latency monitoring, and outage logs.

---

## ⚡ Quick Start for Users (No Coding Required!)

You do **not** need to install Visual Studio, run commands, or touch any code to use this app.

1. **Download the latest executable:**
   - Go to [**Releases**](../../releases/latest) on the right side of this repository.
   - Download the single portable file: **`TMUTarangConnect.exe`**.
2. **Run it:**
   - Double-click `TMUTarangConnect.exe`.
   - On first launch, enter your campus **Registered Mobile Number (with 91 prefix)** and **Password**.
   - Click **Save Account**.
3. **You're all set!**
   - The app minimizes to your system tray (near your taskbar clock).
   - Whenever your PC connects to campus Wi-Fi or Ethernet, it will automatically authenticate in the background.

---

## ✨ Key Features

### 🔄 Multi-Account Credential Management & Auto-Failover
- **Automatic Account Rotation:** When an account hits the 2-device concurrency limit or fails, the engine seamlessly rotates to your next standby account in under 1 second.
- **Credential Pool:** Add multiple accounts with custom labels (e.g. *Primary*, *Mobile*, *Roommate*, *Colleague*). Modify credentials or passwords at any time without dropping your active session.
- **Smooth Session Handover:** When manually switching active accounts, the engine gracefully logs out the previous session and establishes the new one.

### 📊 Real-Time Analytics & Bandwidth Metering
- **Live Speed Benchmarking:** Built-in speed test measures download speed (Mbps), upload speed (Mbps), and ping latency (ms) directly through campus gateway routes.
- **Session & Monthly Metering:** Tracks daily session bandwidth (MB/GB) and cumulative monthly data consumption.
- **Outage Tracking:** Records last outage timestamp, exact downtime duration, and total auto-reconnections today.

### 🔔 Hardware-Accelerated Floating Toast Banners
- Non-intrusive acrylic toast notifications slide in when connecting, switching accounts, or restoring network drops.
- Clickable actions: click the banner anytime to open the Dashboard.

### 🛡️ Privacy & Local Security First
- **Zero Cloud Transmission:** Your credentials never leave your PC. They are sent directly to the local campus FortiGate gateway (`http://172.14.0.1:1000/fgtauth`).
- **No Third-Party Proxies or VPNs:** Uses native Windows HTTP sockets. Does not alter your DNS, network adapter drivers, or routing tables.

---

## 📂 Repository Breakdown

| Folder / File | What it is |
| :--- | :--- |
| **`TMUTarangConnect.exe`** | The complete, standalone application. Contains everything needed to run. |
| **`src/`** | The complete C# / .NET 9 WPF source code. |
| **`src/TMUTarangConnect/`** | The core application project (Views, Models, Services). |
| **`config.example.json`** | A clean configuration template showing the settings structure. |
| **`.gitignore`** | Tells Git to ignore temporary build files, IDE caches, and private user credentials. |
| **`Stop-App.bat`** | A quick helper script to cleanly terminate any running background instances. |

> **Note on `config.json`:** Your active configuration and saved accounts are stored locally on your computer in `config.json`. This file is strictly excluded by `.gitignore` so your personal credentials will never be committed or uploaded to GitHub.

---

## 🛠️ Building From Source (For Developers)

### Prerequisites:
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (or Visual Studio 2022+ with .NET Desktop Development workload).
- Windows 10 (Build 19041+) or Windows 11.

### Build & Publish Commands:
```bash
# Clone the repository
git clone https://github.com/rohitj921/Tarang-Auto-Login.git
cd Tarang-Auto-Login

# Build the Release binary
dotnet build src/TMUTarangConnect/TMUTarangConnect.csproj -c Release

# Publish as a single standalone executable
dotnet publish src/TMUTarangConnect/TMUTarangConnect.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .
```

---

## 📋 System Requirements
- **OS:** Windows 10 / Windows 11 (64-bit)
- **Runtime:** .NET Desktop Runtime 9.0 (included on most modern Windows installs or automatically downloaded by Windows).
- **Network:** Connected to TMU Wi-Fi (`TMU-TARANG`, `TMU TARANG`, `TARANG`) or university campus Ethernet LAN.

---

## 👨‍💻 Developer & Attribution

Developed with ❤️ by **Rohit Jain**
- **GitHub:** [@rohitj921](https://github.com/rohitj921)

---

## ⚖️ License
Distributed under the MIT License. Free for all university employees, faculty, staff, researchers, and campus network users.
