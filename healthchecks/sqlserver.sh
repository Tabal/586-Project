#!/bin/bash
set -eo pipefail

host="localhost"
user="${MSSQL_USER:-sa}"
db="${MSSQL_DB:-master}"
export MSSQL_PASSWORD="${MSSQL_PASSWORD:-YourStrong@Password}"

args=(
	# force SQL Server to not use the local unix socket (test "external" connectibility)
	-S "$host"
	-U "$user"
	-D "$db"
	-Q "SELECT 1"
	-h
)

if select="$(sqlcmd "${args[@]}")" && [ "$select" = '1' ]; then
	exit 0
fi

exit 1