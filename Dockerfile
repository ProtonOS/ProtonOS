# netos Development Environment
# Stage 1: Start with minimal base and essential packages

FROM debian:bookworm-slim

# Prevent interactive prompts
ENV DEBIAN_FRONTEND=noninteractive

# Install core packages available in Debian Bookworm
RUN apt-get update && apt-get install -y --no-install-recommends \
    # Essential utilities
    ca-certificates \
    curl \
    git \
    make \
    file \
    # NASM assembler for nernel (supports win64 output for UEFI)
    nasm \
    # LLVM linker for PE/COFF (lld-link)
    lld \
    # FAT32 image tools (mformat, mcopy, mmd)
    mtools \
    dosfstools \
    # QEMU for x86_64 emulation
    qemu-system-x86 \
    # UEFI firmware
    ovmf \
    && rm -rf /var/lib/apt/lists/*

# Install bflat (C# AOT compiler - not available as package)
ARG BFLAT_VERSION=8.0.2
RUN mkdir -p /opt/bflat \
    && curl -fsSL "https://github.com/bflattened/bflat/releases/download/v${BFLAT_VERSION}/bflat-${BFLAT_VERSION}-linux-glibc-x64.tar.gz" \
    | tar -xzC /opt/bflat \
    && ln -s /opt/bflat/bflat /usr/local/bin/bflat

# Verify tools
RUN echo "=== netos dev environment ===" \
    && nasm --version \
    && nasm -hf | grep win64 \
    && lld-link --version \
    && bflat -v \
    && mtools --version \
    && qemu-system-x86_64 --version | head -1 \
    && ls -la /usr/share/OVMF/

WORKDIR /workspace
CMD ["/bin/bash"]
