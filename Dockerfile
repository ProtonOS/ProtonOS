# ProtonOS Development Environment
# Based on .NET 10 SDK for building bflat from source

FROM mcr.microsoft.com/dotnet/sdk:10.0

# Prevent interactive prompts
ENV DEBIAN_FRONTEND=noninteractive

# Install development tools
# The .NET SDK image is based on Ubuntu 24.04
RUN apt-get update && apt-get install -y --no-install-recommends \
    # Essential utilities
    ca-certificates \
    curl \
    git \
    make \
    file \
    # NASM assembler (supports win64 output for UEFI)
    nasm \
    # LLVM linker for PE/COFF (lld-link) and analysis tools
    lld \
    llvm \
    # libc++ required by bflat's objwriter
    libc++1 \
    # FAT32 image tools (mformat, mcopy, mmd)
    mtools \
    dosfstools \
    # QEMU for x86_64 emulation
    qemu-system-x86 \
    # UEFI firmware
    ovmf \
    # binutils for additional analysis (objdump, readelf)
    binutils \
    && rm -rf /var/lib/apt/lists/*

# Set up bflat build directory
WORKDIR /build/bflat

# Copy bflat source
COPY tools/bflat/ .

# Copy local NuGet package cache
COPY tools/nuget-cache/ /nuget-cache/

# Create nuget.config that uses only the local cache (no GitHub auth needed)
RUN mkdir -p src/bflat && \
    cat > src/bflat/nuget.config << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="/nuget-cache" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

# Build bflat layouts (creates self-contained binaries)
RUN dotnet build src/bflat/bflat.csproj -t:BuildLayouts -c Release

# Install bflat to /opt/bflat
RUN mkdir -p /opt/bflat && \
    cp -r layouts/linux-glibc-x64/* /opt/bflat/ && \
    chmod +x /opt/bflat/bflat && \
    ln -sf /opt/bflat/bflat /usr/local/bin/bflat

# Install dotnet-ildasm tool globally
RUN dotnet tool install --global dotnet-ildasm
ENV PATH="${PATH}:/root/.dotnet/tools"

# Clean up build artifacts
RUN rm -rf /build/bflat /nuget-cache

WORKDIR /workspace

# Verify tools
RUN echo "=== ProtonOS dev environment ===" && \
    nasm --version && \
    nasm -hf | grep win64 && \
    lld-link --version && \
    bflat -v && \
    mtools --version && \
    qemu-system-x86_64 --version | head -1 && \
    ls -la /usr/share/OVMF/ && \
    dotnet --version

CMD ["/bin/bash"]
