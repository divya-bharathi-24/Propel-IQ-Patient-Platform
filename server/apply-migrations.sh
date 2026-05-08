#!/usr/bin/env bash
#
# Propel Database Migration Script
# Applies pending EF Core migrations to the PostgreSQL database
#

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
WHITE='\033[1;37m'
NC='\033[0m' # No Color

echo -e "${CYAN}================================================${NC}"
echo -e "${CYAN}  Propel Database Migration Script${NC}"
echo -e "${CYAN}================================================${NC}"
echo ""

# Change to script directory
cd "$(dirname "$0")"

echo -e "${YELLOW}[1/3] Checking for dotnet CLI...${NC}"
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}ERROR: .NET SDK not found. Please install .NET 10 SDK.${NC}"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo -e "${GREEN}      Found: .NET SDK $DOTNET_VERSION${NC}"
echo ""

echo -e "${YELLOW}[2/3] Checking for pending migrations...${NC}"
if dotnet ef migrations list --project server/Propel.Api.Gateway --no-build &> /dev/null; then
    echo -e "${YELLOW}      Pending migrations detected${NC}"
else
    echo -e "${YELLOW}      Unable to check migrations. Proceeding with update...${NC}"
fi
echo ""

echo -e "${YELLOW}[3/3] Applying migrations to database...${NC}"
echo -e "${WHITE}      Project: Propel.Api.Gateway${NC}"
echo ""

# Apply migrations
if dotnet ef database update --project server/Propel.Api.Gateway --verbose; then
    echo ""
    echo -e "${GREEN}================================================${NC}"
    echo -e "${GREEN}  SUCCESS: Migrations applied successfully!${NC}"
    echo -e "${GREEN}================================================${NC}"
    echo ""
    echo -e "${GREEN}The refresh_tokens table now includes:${NC}"
    echo -e "${WHITE}  - patient_id column (nullable)${NC}"
    echo -e "${WHITE}  - user_id column (now nullable)${NC}"
    echo -e "${WHITE}  - CHECK constraint ensuring exactly one is non-null${NC}"
    echo -e "${WHITE}  - Foreign keys and indexes for patient authentication${NC}"
    echo ""
    echo -e "${CYAN}You can now restart your application.${NC}"
    echo ""
else
    echo ""
    echo -e "${RED}================================================${NC}"
    echo -e "${RED}  ERROR: Migration failed!${NC}"
    echo -e "${RED}================================================${NC}"
    echo ""
    echo -e "${YELLOW}Troubleshooting steps:${NC}"
    echo -e "${WHITE}1. Ensure PostgreSQL is running and accessible${NC}"
    echo -e "${WHITE}2. Check your connection string in appsettings.json${NC}"
    echo -e "${WHITE}3. Verify database credentials are correct${NC}"
    echo -e "${WHITE}4. Check Docker containers if using docker-compose:${NC}"
    echo -e "   ${NC}docker-compose ps${NC}"
    echo ""
    exit 1
fi
