#!/usr/bin/env bash
set -euo pipefail

if [[ $# != 1 || ! -f "$1" || -L "$1" ]]; then
  echo "Pass the existing private temporary config file as the only argument." >&2
  exit 1
fi

config_file="$1"
# The document contains deployment secrets, so restrict access before writing it.
chmod 600 "$config_file"

# Read values from stdin so secrets never appear in jq arguments or shell history.
# Order: subscription, tenant, group, region, registry, SQL server, storage,
# API client ID, staff client ID, staff secret, two service keys, repository.
if ! jq -Rn '
  [inputs] as $values |
  if ($values | length) != 13 then error("Expected thirteen setup values")
  elif any($values[]; type != "string") then error("Setup values must be strings")
  elif ($values[0] | test("^[0-9a-fA-F-]{36}$") | not) then error("Invalid subscription ID")
  elif ($values[1] | test("^[0-9a-fA-F-]{36}$") | not) then error("Invalid tenant ID")
  elif any($values[2:9][]; length == 0) then error("A required setup value is empty")
  elif ($values[9] | length == 0) then error("Staff client secret is empty")
  elif ($values[10] | test("^[A-Za-z0-9_-]{32,128}$") | not) then error("Invalid products API key")
  elif ($values[11] | test("^[A-Za-z0-9_-]{32,128}$") | not) then error("Invalid eligibility API key")
  elif $values[10] == $values[11] then error("Service API keys must differ")
  elif ($values[12] | test("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") | not) then error("Invalid GitHub repository")
  else {
    subscriptionId:$values[0],
    tenantId:$values[1],
    resourceGroup:$values[2],
    location:$values[3],
    registry:$values[4],
    sqlServer:$values[5],
    storageAccount:$values[6],
    mortgageApiClientId:$values[7],
    staffClientId:$values[8],
    staffClientSecret:$values[9],
    productsApiKey:$values[10],
    eligibilityApiKey:$values[11],
    githubRepository:$values[12],
    configureGitHub:true,
    confirmed:true
  } end
' > "$config_file"; then
  echo "Could not create a valid private setup configuration." >&2
  exit 1
fi
