#!/usr/bin/env bash
set -euo pipefail

roslyn_versions=("4.8" "4.12" "5.0")
artifacts_dir="artifacts/generator"
config="${1:-Release}"

echo "=== Multi-Roslyn Pack ==="
echo "Configuration: $config"

rm -rf "$artifacts_dir"

for version in "${roslyn_versions[@]}"; do
  echo "--- Building generator for Roslyn $version ---"

  # Clean obj/bin to prevent artifact contamination between Roslyn versions
  rm -rf src/ForgeMap.Generator/obj src/ForgeMap.Generator/bin
  rm -rf src/ForgeMap.Abstractions/obj src/ForgeMap.Abstractions/bin

  dotnet build src/ForgeMap.Generator/ForgeMap.Generator.csproj \
    -c "$config" \
    -p:ROSLYN_VERSION="$version"

  mkdir -p "$artifacts_dir/roslyn$version"
  cp "src/ForgeMap.Generator/bin/$config/netstandard2.0/ForgeMap.Generator.dll" \
     "$artifacts_dir/roslyn$version/"
  cp "src/ForgeMap.Generator/bin/$config/netstandard2.0/ForgeMap.Abstractions.dll" \
     "$artifacts_dir/roslyn$version/"
done

echo "--- Building ForgeMap wrapper ---"
dotnet build src/ForgeMap/ForgeMap.csproj -c "$config"

echo "--- Packing ---"
pack_args=(-c "$config" --no-build "-p:GeneratorArtifactsDir=$(pwd)/$artifacts_dir")
if [ -n "${PACKAGE_VERSION:-}" ]; then
  pack_args+=("-p:PackageVersion=$PACKAGE_VERSION")
fi
dotnet pack src/ForgeMap/ForgeMap.csproj "${pack_args[@]}"

echo "=== Pack complete ==="
ls -la artifacts/*.nupkg 2>/dev/null || ls -la src/ForgeMap/bin/"$config"/*.nupkg 2>/dev/null || echo "NuGet package location may vary — check bin/$config/"
