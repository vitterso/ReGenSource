#!/bin/bash

localNugetVersion="1.0.0-local"
localNugetRepo=~/nugetpackages
localNugetSourceName="local"
newProjectFile="MyTestApplication2.csproj"

cd src/MyTestApplication

mkdir $localNugetRepo
dotnet pack ../ReGenSource/ReGenSource.csproj -c Release -o "$localNugetRepo" /p:Version="$localNugetVersion"
dotnet nuget add source --name "$localNugetSourceName" "$localNugetRepo"

cp MyTestApplication.csproj "$newProjectFile"
sed -i "s#<ProjectReference.*#<PackageReference Include=\"\ReGenSource\" Version=\"$localNugetVersion\" />#" "$newProjectFile"

dotnet build "$newProjectFile"
exit_code=$?

dotnet nuget remove source "$localNugetSourceName"
rm -rf $localNugetRepo
rm "$newProjectFile"

cd ../..

exit $exit_code
