SELECT format('CREATE ROLE %I WITH LOGIN PASSWORD %L', :'app_user', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user')
\gexec

SELECT format('ALTER ROLE %I WITH LOGIN PASSWORD %L', :'app_user', :'app_password')
\gexec

SELECT format('CREATE DATABASE %I WITH OWNER %I', :'app_database', :'app_user')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'app_database')
\gexec

SELECT format('GRANT ALL PRIVILEGES ON DATABASE %I TO %I', :'app_database', :'app_user')
\gexec

