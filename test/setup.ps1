param(
    [string]$pw="$env:SA_PASSWORD",
    [string]$sqlpid="Express",
    [string]$tag="2017-latest",
    [int]$port=1433,
    [string]$dbname="TaskHub",
    [string]$collation="Latin1_General_100_BIN2"
)

Write-Host "Pulling down the mcr.microsoft.com/mssql/server:$tag image..."
docker pull mcr.microsoft.com/mssql/server:$tag

# Start the SQL Server 2017 docker container with the Express edition
Write-Host "Starting SQL Server $tag $sqlpid docker container on port $port" -ForegroundColor DarkYellow
docker run --name mssql-server -e 'ACCEPT_EULA=Y' -e "SA_PASSWORD=$pw" -e "MSSQL_PID=$sqlpid" -p ${port}:1433 -d mcr.microsoft.com/mssql/server:$tag

# The container needs a bit more time before it can start accepting commands
Write-Host "Sleeping for 10 seconds to let the container finish initializing..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# Create the database with strict binary collation
Write-Host "Creating '$dbname' database with '$collation' collation" -ForegroundColor DarkYellow
docker exec -d mssql-server /opt/mssql-tools/bin/sqlcmd -S . -U sa -P "$pw" -Q "CREATE DATABASE [$dbname] COLLATE $collation"