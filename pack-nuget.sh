#!/usr/bin/env zsh
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "$0")" && pwd -P)"
project="$script_dir/fluid-script/FluidScript.csproj"
output="$script_dir/artifacts/nuget"

if [[ ! -f "$project" ]]; then
    print -u2 "Could not find the FluidScript project: $project"
    exit 1
fi

current_version="$(sed -nE 's@^[[:space:]]*<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>[[:space:]]*$@\1@p' "$project")"
if [[ ! "$current_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    print -u2 "The project Version must use major.minor.patch format."
    exit 1
fi

IFS='.' read -r major minor patch <<< "$current_version"
next_version="${major}.${minor}.$((10#$patch + 1))"

print "Packing FluidScript $next_version..."
mkdir -p "$output"
dotnet pack "$project" --configuration Release --output "$output" --p:Version="$next_version"

sed -i '' "s|<Version>$current_version</Version>|<Version>$next_version</Version>|" "$project"
updated_version="$(sed -nE 's@^[[:space:]]*<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>[[:space:]]*$@\1@p' "$project")"
if [[ "$updated_version" != "$next_version" ]]; then
    print -u2 "Package was created, but the project version could not be updated."
    exit 1
fi

print "Created $output/FluidScript.$next_version.nupkg"
