# Zeniqa Download Manager

A modern, user-friendly Windows application for managing and downloading your favorite videos and files efficiently.

---

## 🚀 Features
- Beautiful, modern WPF UI
- Download videos  from direct links (MP4, MKV, etc.)
- Chunked (multi-part) downloads for speed, with automatic fallback for reliability
- YouTube and streaming support via [yt-dlp](https://github.com/yt-dlp/yt-dlp) (external dependency)
- Download queue management (pause, resume, retry, clear)
- Real-time progress bars and status updates
- File size, type, and metadata analysis
- Settings for concurrency, buffer size, and more


---



---

## 🛠️ Build & Run

### Prerequisites
- [.NET 7.0+ SDK](https://dotnet.microsoft.com/download)
- Windows 10/11
- (Optional, for YouTube): [yt-dlp](https://github.com/yt-dlp/yt-dlp) in your PATH

### Build
```sh
dotnet build ZeniqaDownloadManager
```

### Run
```sh
dotnet run --project ZeniqaDownloadManager
```

---

## 🤝 Contributing
We welcome contributions! To get started:
1. Fork this repo
2. Create a new branch (`git checkout -b feature/your-feature`)
3. Make your changes
4. Commit and push (`git commit -am 'Add new feature' && git push`)
5. Open a Pull Request

Please open issues for bugs, feature requests, or questions.

---

## 📄 License
MIT

---

## 📂 .gitignore
This project uses a standard .NET .gitignore. Make sure your repository includes:

```
bin/
obj/
*.user
*.suo
*.userosscache
*.sln.docstates
.vs/
```

---

## 💡 Future Enhancements
- Replace MessageBox popups with a custom notification system for a more modern user experience.

---

## 📬 Contact
- **Email:** zeniqa@gmail.com
- **Mobile:** +250791597890

---

**Zeniqa Download Manager** — Modern, fast, and open for collaboration! 
