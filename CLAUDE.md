# Windows Environment Notice

This is a Windows machine. When running commands:
- Use `/dev/null` instead of `nul` for null device redirection
- Prefer PowerShell-style commands over Unix commands when possible
- Do NOT use `> nul` or `2>nul` redirections - use `> /dev/null` or `2>/dev/null` which Git Bash handles correctly
- Avoid Unix commands like `ls`, `cat`, `rm` - use Windows equivalents or PowerShell