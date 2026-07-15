#!/bin/bash
#
# build-extension.sh - Build and package SimpleModelServer VS Extension VSIX
#
# Usage: ./build-extension.sh [OPTIONS]
#
# Options:
#   -c, --config CONFIG        Build configuration (Debug/Release). Default: Release
#   -v, --version VERSION      Override semantic version (e.g., 1.2.3)
#   -o, --output DIR           Output directory for artifacts. Default: build-output
#   -p, --platform PLATFORM    Target platform (AnyCPU/x64/ARM64). Default: AnyCPU
#   -s, --sign                 Enable VSIX signing (requires certificate)
#   -k, --checksum            Create SHA256 checksums. Default: true
#   --no-checksum             Don't create checksums
#   --verbose                 Enable verbose output
#   -h, --help                Show this help message
#

set -euo pipefail

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

# Configuration variables
CONFIGURATION="Release"
VERSION=""
OUTPUT_DIR="build-output"
PLATFORM="AnyCPU"
SIGN_VSIX=false
CREATE_CHECKSUM=true
VERBOSE=false
LOG_FILE="build.log"

# Logging functions
log_section() {
    echo -e "${MAGENTA}================================${NC}"
    echo -e "${MAGENTA}$1${NC}"
    echo -e "${MAGENTA}================================${NC}"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_info() {
    echo -e "${CYAN}[INFO]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

# Help function
show_help() {
    grep "^#" "$0" | grep -v "^#!/bin/bash" | sed 's/^#\s*//' | head -30
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -c|--config)
            CONFIGURATION="$2"
            shift 2
            ;;
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        -o|--output)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        -p|--platform)
            PLATFORM="$2"
            shift 2
            ;;
        -s|--sign)
            SIGN_VSIX=true
            shift
            ;;
        -k|--checksum)
            CREATE_CHECKSUM=true
            shift
            ;;
        --no-checksum)
            CREATE_CHECKSUM=false
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            show_help
            exit 1
            ;;
    esac
done

# Determine script directory and paths
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
VS_EXTENSION_PATH="$PROJECT_ROOT/src/Extensions/SimpleModelServer.Extensions.VS"
VS_EXTENSION_CSPROJ="$VS_EXTENSION_PATH/SimpleModelServer.Extensions.VS.csproj"

# Validate project exists
if [[ ! -f "$VS_EXTENSION_CSPROJ" ]]; then
    log_error "VS Extension project not found: $VS_EXTENSION_CSPROJ"
    exit 1
fi

# Create output directory
mkdir -p "$OUTPUT_DIR"
CONFIG_OUTPUT_DIR="$OUTPUT_DIR/$CONFIGURATION"
mkdir -p "$CONFIG_OUTPUT_DIR"

# Log to file
exec 1> >(tee -a "$LOG_FILE")
exec 2>&1

log_section "SimpleModelServer VS Extension Build"

# Display build parameters
log_info "Configuration: $CONFIGURATION"
log_info "Platform: $PLATFORM"
log_info "Output Directory: $(cd "$OUTPUT_DIR" && pwd)"
[[ -n "$VERSION" ]] && log_info "Version Override: $VERSION"

# Step 1: Clean previous builds
log_section "Step 1: Cleaning Previous Builds"
{
    BIN_PATH="$VS_EXTENSION_PATH/bin/$CONFIGURATION"
    OBJ_PATH="$VS_EXTENSION_PATH/obj/$CONFIGURATION"

    if [[ -d "$BIN_PATH" ]]; then
        rm -rf "$BIN_PATH"
        log_success "Cleaned bin directory"
    fi
    if [[ -d "$OBJ_PATH" ]]; then
        rm -rf "$OBJ_PATH"
        log_success "Cleaned obj directory"
    fi
} || {
    log_error "Failed to clean directories"
    exit 1
}

# Step 2: Restore NuGet packages
log_section "Step 2: Restoring NuGet Packages"
{
    log_info "Restoring project packages"
    dotnet restore "$VS_EXTENSION_CSPROJ"
    log_success "Project packages restored"
} || {
    log_error "Failed to restore packages: $?"
    exit 1
}

# Step 3: Build the project
log_section "Step 3: Building VS Extension"
{
    BUILD_ARGS="build $VS_EXTENSION_CSPROJ --configuration $CONFIGURATION --no-restore -p:Platform=$PLATFORM"

    [[ -n "$VERSION" ]] && BUILD_ARGS="$BUILD_ARGS -p:Version=$VERSION"
    [[ "$VERBOSE" == true ]] && BUILD_ARGS="$BUILD_ARGS --verbosity detailed"

    log_info "Building: dotnet $BUILD_ARGS"
    eval "dotnet $BUILD_ARGS"

    log_success "Build completed successfully"
} || {
    log_error "Build failed"
    exit 1
}

# Step 4: Locate and verify VSIX package
log_section "Step 4: Locating VSIX Package"
{
    VSIX_PATH=$(find "$VS_EXTENSION_PATH/bin/$CONFIGURATION" -name "*.vsix" -type f 2>/dev/null | head -1)

    if [[ -z "$VSIX_PATH" ]]; then
        log_error "No VSIX file found in bin output directory"
        exit 1
    fi

    VSIX_NAME=$(basename "$VSIX_PATH")
    VSIX_SIZE=$(du -h "$VSIX_PATH" | cut -f1)

    log_success "Found VSIX: $VSIX_NAME"
    log_info "Size: $VSIX_SIZE"
} || {
    log_error "Failed to locate VSIX: $?"
    exit 1
}

# Step 5: Copy artifacts to output directory
log_section "Step 5: Copying Artifacts"
{
    DESTINATION_PATH="$CONFIG_OUTPUT_DIR/$VSIX_NAME"
    cp -f "$VSIX_PATH" "$DESTINATION_PATH"
    log_success "Copied VSIX to: $DESTINATION_PATH"

    # Copy XML documentation if available
    XML_PATH="${VSIX_PATH%.vsix}.xml"
    if [[ -f "$XML_PATH" ]]; then
        cp -f "$XML_PATH" "$CONFIG_OUTPUT_DIR/$(basename "$XML_PATH")"
        log_success "Copied documentation XML"
    fi
} || {
    log_error "Failed to copy artifacts: $?"
    exit 1
}

# Step 6: Create checksums
if [[ "$CREATE_CHECKSUM" == true ]]; then
    log_section "Step 6: Creating Checksums"
    {
        VSIX_FOR_CHECKSUM="$CONFIG_OUTPUT_DIR/$VSIX_NAME"
        CHECKSUM_FILE="${VSIX_FOR_CHECKSUM%.vsix}.vsix.sha256"

        # Create SHA256 checksum
        if command -v sha256sum &> /dev/null; then
            HASH=$(sha256sum "$VSIX_FOR_CHECKSUM" | awk '{print $1}')
        elif command -v shasum &> /dev/null; then
            HASH=$(shasum -a 256 "$VSIX_FOR_CHECKSUM" | awk '{print $1}')
        else
            log_warning "sha256sum/shasum not found, skipping checksum"
            HASH="N/A"
        fi

        if [[ "$HASH" != "N/A" ]]; then
            echo "$HASH  $VSIX_NAME" > "$CHECKSUM_FILE"
            log_success "SHA256: $HASH"
            log_info "Checksum file: $CHECKSUM_FILE"
        fi
    } || {
        log_warning "Failed to create checksums: $?"
    }
fi

# Step 7: Generate build report
log_section "Step 7: Generating Build Report"
{
    REPORT_PATH="$CONFIG_OUTPUT_DIR/BUILD_REPORT.txt"
    BUILD_TIME=$(date '+%Y-%m-%d %H:%M:%S')

    cat > "$REPORT_PATH" << EOF
SimpleModelServer VS Extension Build Report
============================================
Generated: $BUILD_TIME

Build Configuration
-------------------
Configuration:     $CONFIGURATION
Platform:          $PLATFORM
Version:           $([ -n "$VERSION" ] && echo "$VERSION" || echo "From version.props")
Output Directory:  $(cd "$CONFIG_OUTPUT_DIR" && pwd)

Artifacts
---------
VSIX File:         $VSIX_NAME
VSIX Path:         $DESTINATION_PATH
VSIX Size:         $VSIX_SIZE
Build Time:        $BUILD_TIME

Build Command
-------------
dotnet build $VS_EXTENSION_CSPROJ --configuration $CONFIGURATION --platform $PLATFORM

Documentation
--------------
For more information, see: $PROJECT_ROOT/src/Extensions/SimpleModelServer.Extensions.VS/README.md

Release Notes
-------------
See: https://github.com/rsperry79/SimpleModelServer/releases
EOF

    log_success "Report generated: $REPORT_PATH"
} || {
    log_warning "Failed to generate report: $?"
}

# Step 8: Summary
log_section "Build Complete"
log_success "VSIX package successfully created!"
log_info "Location: $(cd "$CONFIG_OUTPUT_DIR" && pwd)"
echo ""
log_info "To install the extension:"
log_info "  1. Open Visual Studio 2022 (17.14+)"
log_info "  2. Extensions > Manage Extensions > Install from File"
log_info "  3. Select: $DESTINATION_PATH"

exit 0
