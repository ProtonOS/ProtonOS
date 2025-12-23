#!/bin/bash
# ProtonOS dependency installer
# Installs all required packages for building ProtonOS and its toolchain

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

info() { echo -e "${GREEN}[INFO]${NC} $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; exit 1; }

# Check if running as root or can sudo
check_sudo() {
    if [ "$EUID" -eq 0 ]; then
        SUDO=""
    elif command -v sudo &> /dev/null; then
        SUDO="sudo"
    else
        error "This script requires root privileges or sudo"
    fi
}

# Detect package manager
detect_package_manager() {
    if command -v apt &> /dev/null; then
        PKG_MGR="apt"
    elif command -v dnf &> /dev/null; then
        PKG_MGR="dnf"
    elif command -v pacman &> /dev/null; then
        PKG_MGR="pacman"
    else
        error "Unsupported package manager. Please install dependencies manually."
    fi
    info "Detected package manager: $PKG_MGR"
}

# Check if a command exists
has_cmd() {
    command -v "$1" &> /dev/null
}

# Check if a package is installed (apt)
apt_has_pkg() {
    dpkg -l "$1" 2>/dev/null | grep -q "^ii"
}

# Install system packages for Debian/Ubuntu
install_apt_packages() {
    local packages=()

    # Runtime build dependencies
    apt_has_pkg cmake || packages+=(cmake)
    apt_has_pkg llvm || packages+=(llvm)
    apt_has_pkg lld || packages+=(lld)
    apt_has_pkg clang || packages+=(clang)
    apt_has_pkg build-essential || packages+=(build-essential)
    apt_has_pkg python-is-python3 || packages+=(python-is-python3)
    apt_has_pkg curl || packages+=(curl)
    apt_has_pkg git || packages+=(git)
    apt_has_pkg libicu-dev || packages+=(libicu-dev)
    apt_has_pkg liblttng-ust-dev || packages+=(liblttng-ust-dev)
    apt_has_pkg libssl-dev || packages+=(libssl-dev)
    apt_has_pkg libkrb5-dev || packages+=(libkrb5-dev)
    apt_has_pkg ninja-build || packages+=(ninja-build)
    apt_has_pkg cpio || packages+=(cpio)

    # ProtonOS build dependencies
    apt_has_pkg nasm || packages+=(nasm)
    apt_has_pkg mtools || packages+=(mtools)
    apt_has_pkg qemu-system-x86 || packages+=(qemu-system-x86)
    apt_has_pkg ovmf || packages+=(ovmf)

    if [ ${#packages[@]} -eq 0 ]; then
        info "All system packages already installed"
    else
        info "Installing packages: ${packages[*]}"
        $SUDO apt update
        $SUDO apt install -y "${packages[@]}"
    fi
}

# Install system packages for Fedora
install_dnf_packages() {
    local packages=()

    # Runtime build dependencies
    has_cmd cmake || packages+=(cmake)
    has_cmd llvm-config || packages+=(llvm)
    has_cmd lld || packages+=(lld)
    has_cmd clang || packages+=(clang)
    has_cmd python3 || packages+=(python)
    has_cmd curl || packages+=(curl)
    has_cmd git || packages+=(git)
    has_cmd ninja || packages+=(ninja-build)
    packages+=(libicu-devel openssl-devel krb5-devel lttng-ust-devel cpio)

    # ProtonOS build dependencies
    has_cmd nasm || packages+=(nasm)
    has_cmd mformat || packages+=(mtools)
    has_cmd qemu-system-x86_64 || packages+=(qemu-system-x86)
    packages+=(edk2-ovmf)

    info "Installing packages: ${packages[*]}"
    $SUDO dnf install -y "${packages[@]}"
}

# Install system packages for Arch
install_pacman_packages() {
    local packages=()

    # Runtime build dependencies
    has_cmd cmake || packages+=(cmake)
    has_cmd llvm-config || packages+=(llvm)
    has_cmd lld || packages+=(lld)
    has_cmd clang || packages+=(clang)
    has_cmd python3 || packages+=(python)
    has_cmd curl || packages+=(curl)
    has_cmd git || packages+=(git)
    has_cmd ninja || packages+=(ninja)
    packages+=(icu openssl krb5 cpio base-devel)

    # ProtonOS build dependencies
    has_cmd nasm || packages+=(nasm)
    has_cmd mformat || packages+=(mtools)
    has_cmd qemu-system-x86_64 || packages+=(qemu)
    packages+=(edk2-ovmf)

    info "Installing packages: ${packages[*]}"
    $SUDO pacman -S --needed --noconfirm "${packages[@]}"
}

# Check .NET SDK version
check_dotnet_version() {
    local version="$1"
    if has_cmd dotnet; then
        dotnet --list-sdks 2>/dev/null | grep -q "^${version}\." && return 0
    fi
    return 1
}

# Install .NET SDK
install_dotnet() {
    local version="$1"

    if check_dotnet_version "$version"; then
        info ".NET SDK $version already installed"
        return 0
    fi

    info "Installing .NET SDK $version..."

    # .NET 10 is in preview - must use install script (not in stable repos)
    # For stable versions (8.x, 9.x), we could use apt, but the install script
    # works universally and handles preview versions correctly
    install_dotnet_script "$version"
}

# Install .NET using Microsoft's install script
install_dotnet_script() {
    local version="$1"
    local install_dir

    # Use system location for root, user location otherwise
    if [ "$EUID" -eq 0 ]; then
        install_dir="/usr/share/dotnet"
    else
        install_dir="$HOME/.dotnet"
    fi

    info "Using Microsoft install script for .NET SDK $version..."
    info "Install location: $install_dir"

    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh

    /tmp/dotnet-install.sh --channel "$version" --install-dir "$install_dir"
    rm /tmp/dotnet-install.sh

    # Set up environment
    export DOTNET_ROOT="$install_dir"
    export PATH="$install_dir:$PATH"

    # Add to shell config if not already there
    local shell_rc="$HOME/.bashrc"
    if [[ ":$PATH:" != *":$install_dir:"* ]] || ! grep -q "DOTNET_ROOT" "$shell_rc" 2>/dev/null; then
        {
            echo ""
            echo "# .NET SDK"
            echo "export DOTNET_ROOT=\"$install_dir\""
            echo "export PATH=\"$install_dir:\$PATH\""
        } >> "$shell_rc"
        warn "Added dotnet to PATH in ~/.bashrc. Run 'source ~/.bashrc' or start a new shell."
    fi
}

# Install dotnet-ildasm tool
install_ildasm() {
    # Ensure dotnet tools are in PATH
    local tools_path="$HOME/.dotnet/tools"
    if [[ ":$PATH:" != *":$tools_path:"* ]]; then
        export PATH="$tools_path:$PATH"
    fi

    if dotnet tool list -g 2>/dev/null | grep -q "dotnet-ildasm"; then
        info "dotnet-ildasm already installed"
    else
        info "Installing dotnet-ildasm tool..."
        dotnet tool install -g dotnet-ildasm || warn "Failed to install dotnet-ildasm (may already exist)"
    fi
}

# Main
main() {
    info "ProtonOS Dependency Installer"
    echo ""

    check_sudo
    detect_package_manager

    # Install system packages
    info "Checking system packages..."
    case "$PKG_MGR" in
        apt) install_apt_packages ;;
        dnf) install_dnf_packages ;;
        pacman) install_pacman_packages ;;
    esac

    # Install .NET SDK 10.0
    info "Checking .NET SDK..."
    install_dotnet "10.0"

    # Install dotnet-ildasm
    install_ildasm

    echo ""
    info "All dependencies installed successfully!"

    # Verify key tools
    echo ""
    info "Verification:"
    echo "  dotnet: $(dotnet --version 2>/dev/null || echo 'NOT FOUND')"
    echo "  nasm:   $(nasm --version 2>/dev/null | head -1 || echo 'NOT FOUND')"
    echo "  clang:  $(clang --version 2>/dev/null | head -1 || echo 'NOT FOUND')"
    echo "  cmake:  $(cmake --version 2>/dev/null | head -1 || echo 'NOT FOUND')"
    echo "  lld:    $(ld.lld --version 2>/dev/null | head -1 || echo 'NOT FOUND')"
    echo "  qemu:   $(qemu-system-x86_64 --version 2>/dev/null | head -1 || echo 'NOT FOUND')"
}

main "$@"
