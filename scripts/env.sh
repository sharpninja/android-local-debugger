#!/usr/bin/env bash
# Source this file in the managed cloud environment.
export DOTNET_ROOT=/workspace/.cloud-setup/dotnet
export DOTNET_CLI_HOME=/workspace/.cloud-setup/dotnet-home
export NUGET_PACKAGES=/workspace/.cloud-setup/nuget-packages
export NUGET_SCRATCH=/workspace/.cloud-setup/nuget-scratch
export XDG_RUNTIME_DIR=/workspace/.cloud-setup/xdg-runtime
export XDG_DATA_HOME=/workspace/.cloud-setup/xdg-data
export XDG_CACHE_HOME=/workspace/.cloud-setup/xdg-cache
export XDG_CONFIG_HOME=/workspace/.cloud-setup/xdg-config
export JAVA_HOME=/workspace/.cloud-setup/jdk21
export ANDROID_HOME=/workspace/.cloud-setup/android-sdk
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export ANDROID_USER_HOME=/workspace/.cloud-setup/android-user-home
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_NUGET_SIGNATURE_VERIFICATION=true
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true
export PATH="$DOTNET_ROOT:$JAVA_HOME/bin:$ANDROID_HOME/platform-tools:$PATH"
mkdir -p "$XDG_RUNTIME_DIR" "$XDG_DATA_HOME" "$XDG_CACHE_HOME" "$XDG_CONFIG_HOME" "$ANDROID_USER_HOME" "$NUGET_SCRATCH"
chmod 700 "$XDG_RUNTIME_DIR"
