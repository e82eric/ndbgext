dotnet clean ndbgext.sln
dotnet build ndbgext.sln
dotnet publish -r win-x64 -c Debug ndbgext.sln
