#!/bin/bash
set -e

echo "Creating lib directory..."
mkdir -p lib

echo "Downloading Rust game assemblies using DepotDownloader..."

# Download Rust dedicated server files (contains the assemblies we need)
# App ID: 258550 (Rust Dedicated Server)
# Depot ID: 258551 (Windows server files)
./DepotDownloader/DepotDownloader -app 258550 -depot 258551 -dir ./rust_temp -filelist filelist.txt

echo "Copying required assemblies..."
cp rust_temp/RustDedicated_Data/Managed/Assembly-CSharp.dll lib/ || echo "Warning: Assembly-CSharp.dll not found"
cp rust_temp/RustDedicated_Data/Managed/Facepunch.Sqlite.dll lib/ || echo "Warning: Facepunch.Sqlite.dll not found"
cp rust_temp/RustDedicated_Data/Managed/UnityEngine.CoreModule.dll lib/ || echo "Warning: UnityEngine.CoreModule.dll not found"

echo "Cleaning up..."
rm -rf rust_temp

echo "Done! Required assemblies are in the lib/ directory"
