pack-packages:
	@echo "Packing packages..."
	rm -rf nupkgs
	dotnet pack src/LiteHttp/LiteHttp.csproj --configuration Release --output nupkgs;
	@echo "Packing completed. Packages are available in the 'nupkgs' directory."