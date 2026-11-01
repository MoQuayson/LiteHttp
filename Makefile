pack-packages:
	@echo "Packing packages..."
	rm -rf nupkgs
	dotnet clean src/LiteHttp/LiteHttp.csproj --configuration Release
	dotnet build src/LiteHttp/LiteHttp.csproj --configuration Release --no-incremental
	dotnet pack src/LiteHttp/LiteHttp.csproj --configuration Release --no-build --output nupkgs
	@echo "Packing completed. Packages are available in the 'nupkgs' directory."
