// ProtonOS kernel - System Call Numbers
// Linux-compatible syscall numbers for user-space compatibility.

namespace ProtonOS.Syscall;

/// <summary>
/// System call numbers (Linux x86_64 ABI compatible where practical)
/// </summary>
public static class SyscallNumbers
{
    // ==================== File I/O ====================
    public const int SYS_READ = 0;
    public const int SYS_WRITE = 1;
    public const int SYS_OPEN = 2;
    public const int SYS_CLOSE = 3;
    public const int SYS_STAT = 4;
    public const int SYS_FSTAT = 5;
    public const int SYS_LSTAT = 6;
    public const int SYS_POLL = 7;
    public const int SYS_LSEEK = 8;
    public const int SYS_MMAP = 9;
    public const int SYS_MPROTECT = 10;
    public const int SYS_MUNMAP = 11;
    public const int SYS_BRK = 12;
    public const int SYS_MSYNC = 26;
    public const int SYS_MREMAP = 25;
    public const int SYS_IOCTL = 16;
    public const int SYS_ACCESS = 21;
    public const int SYS_PIPE = 22;
    public const int SYS_DUP = 32;
    public const int SYS_DUP2 = 33;

    // ==================== Directory Operations ====================
    public const int SYS_GETDENTS = 78;
    public const int SYS_GETDENTS64 = 217;
    public const int SYS_GETCWD = 79;
    public const int SYS_CHDIR = 80;
    public const int SYS_FCHDIR = 81;
    public const int SYS_RENAME = 82;
    public const int SYS_MKDIR = 83;
    public const int SYS_RMDIR = 84;
    public const int SYS_CREAT = 85;
    public const int SYS_LINK = 86;
    public const int SYS_UNLINK = 87;
    public const int SYS_SYMLINK = 88;
    public const int SYS_READLINK = 89;
    public const int SYS_CHMOD = 90;
    public const int SYS_FCHMOD = 91;
    public const int SYS_CHOWN = 92;
    public const int SYS_FCHOWN = 93;
    public const int SYS_LCHOWN = 94;

    // ==================== Process Control ====================
    public const int SYS_EXIT = 60;
    public const int SYS_FORK = 57;
    public const int SYS_VFORK = 58;
    public const int SYS_EXECVE = 59;
    public const int SYS_WAIT4 = 61;
    public const int SYS_KILL = 62;
    public const int SYS_GETPID = 39;
    public const int SYS_GETPPID = 110;
    public const int SYS_GETPGID = 121;
    public const int SYS_SETPGID = 109;
    public const int SYS_GETSID = 124;
    public const int SYS_SETSID = 112;

    // ==================== User/Group Identity ====================
    public const int SYS_GETUID = 102;
    public const int SYS_GETGID = 104;
    public const int SYS_GETEUID = 107;
    public const int SYS_GETEGID = 108;
    public const int SYS_SETUID = 105;
    public const int SYS_SETGID = 106;
    public const int SYS_SETREUID = 113;
    public const int SYS_SETREGID = 114;
    public const int SYS_GETGROUPS = 115;
    public const int SYS_SETGROUPS = 116;

    // ==================== Signals ====================
    public const int SYS_RT_SIGACTION = 13;
    public const int SYS_RT_SIGPROCMASK = 14;
    public const int SYS_RT_SIGRETURN = 15;
    public const int SYS_RT_SIGPENDING = 127;
    public const int SYS_RT_SIGSUSPEND = 130;
    public const int SYS_RT_SIGTIMEDWAIT = 128;

    // ==================== Sockets ====================
    public const int SYS_SOCKET = 41;
    public const int SYS_CONNECT = 42;
    public const int SYS_ACCEPT = 43;
    public const int SYS_SENDTO = 44;
    public const int SYS_RECVFROM = 45;
    public const int SYS_SENDMSG = 46;
    public const int SYS_RECVMSG = 47;
    public const int SYS_SHUTDOWN = 48;
    public const int SYS_BIND = 49;
    public const int SYS_LISTEN = 50;
    public const int SYS_GETSOCKNAME = 51;
    public const int SYS_GETPEERNAME = 52;
    public const int SYS_SOCKETPAIR = 53;
    public const int SYS_SETSOCKOPT = 54;
    public const int SYS_GETSOCKOPT = 55;

    // ==================== Time ====================
    public const int SYS_NANOSLEEP = 35;
    public const int SYS_GETITIMER = 36;
    public const int SYS_ALARM = 37;
    public const int SYS_SETITIMER = 38;
    public const int SYS_GETTIMEOFDAY = 96;
    public const int SYS_SETTIMEOFDAY = 164;
    public const int SYS_CLOCK_GETTIME = 228;
    public const int SYS_CLOCK_SETTIME = 227;
    public const int SYS_CLOCK_GETRES = 229;

    // ==================== System Info ====================
    public const int SYS_UNAME = 63;
    public const int SYS_GETRLIMIT = 97;
    public const int SYS_SETRLIMIT = 160;
    public const int SYS_GETRUSAGE = 98;
    public const int SYS_SYSINFO = 99;

    // ==================== Misc ====================
    public const int SYS_UMASK = 95;
    public const int SYS_PRCTL = 157;
    public const int SYS_ARCH_PRCTL = 158;
    public const int SYS_EXIT_GROUP = 231;
    public const int SYS_GETRANDOM = 318;

    // ==================== Maximum syscall number ====================
    public const int SYS_MAX = 512;
}
