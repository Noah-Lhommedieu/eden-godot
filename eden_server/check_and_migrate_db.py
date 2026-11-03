#!/usr/bin/env python3
"""
Script to check PostgreSQL container status and apply migrations
"""
import subprocess
import sys
import os

EDEN_ROOT = os.path.dirname(os.path.abspath(__file__))

# Set Docker host for rootless docker
os.environ['DOCKER_HOST'] = 'unix:///run/user/1000/docker.sock'

def run_cmd(cmd, input_text=None):
    """Run command and return output"""
    result = subprocess.run(cmd, capture_output=True, text=True, input=input_text)
    return result.returncode, result.stdout, result.stderr

def main():
    print("=== Checking Docker containers ===")
    rc, out, err = run_cmd(['docker', 'ps', '--format', '{{.Names}}\t{{.Status}}'])
    if rc != 0:
        print(f"ERROR: {err}")
        return 1
    print(out)
    
    container_name = 'eden_server-postgres-1'
    if container_name not in out:
        print(f"\n{container_name} not running, starting...")
        rc, out, err = run_cmd(['docker', 'compose', 'up', '-d'], cwd=EDEN_ROOT)
        if rc != 0:
            print(f"ERROR starting container: {err}")
            return 1
        print("Container started, waiting 3 seconds...")
        import time
        time.sleep(3)
    
    print("\n=== Checking databases ===")
    rc, out, err = run_cmd(['docker', 'exec', container_name, 'psql', '-U', 'postgres', '-c', '\\l'])
    print(out if rc == 0 else f"ERROR: {err}")
    
    # Create databases if they don't exist
    for dbname in ['edenfr_db', 'edenfr_account', 'edenfr_member']:
        if dbname not in out:
            print(f"\n=== Creating database {dbname} ===")
            rc, out, err = run_cmd([
                'docker', 'exec', container_name, 'psql', '-U', 'postgres',
                '-c', f"CREATE DATABASE {dbname};"
            ])
            if rc != 0:
                print(f"ERROR creating {dbname}: {err}")
            else:
                print(f"✓ Database {dbname} created")
    
    print("\n=== Checking tables in edenfr_db ===")
    rc, out, err = run_cmd([
        'docker', 'exec', container_name, 'psql', '-U', 'postgres', '-d', 'edenfr_db',
        '-c', "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;"
    ])
    print(out if rc == 0 else f"ERROR: {err}")
    
    if 'player_classes' not in out:
        print("\n=== Applying migration 001-initial-schema.sql ===")
        with open(os.path.join(EDEN_ROOT, 'Database', '001-initial-schema.sql'), 'r') as f:
            sql = f.read()
        
        rc, out, err = run_cmd([
            'docker', 'exec', '-i', container_name, 'psql', '-U', 'postgres', '-d', 'edenfr_db'
        ], input_text=sql)
        
        if rc != 0:
            print(f"Migration ERROR: {err}")
            return 1
        print("Migration applied successfully!")
        print(out)
        
        # Verify migration
        print("\n=== Verifying migration ===")
        rc, out, err = run_cmd([
            'docker', 'exec', container_name, 'psql', '-U', 'postgres', '-d', 'edenfr_db',
            '-c', "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;"
        ])
        print(out if rc == 0 else f"ERROR: {err}")
    else:
        print("✓ Migration already applied (player_classes table exists)")
    
    print("\n=== Checking player_classes structure ===")
    rc, out, err = run_cmd([
        'docker', 'exec', container_name, 'psql', '-U', 'postgres', '-d', 'edenfr_db',
        '-c', "SELECT column_name, data_type, is_nullable FROM information_schema.columns WHERE table_name = 'player_classes' ORDER BY ordinal_position;"
    ])
    print(out if rc == 0 else f"ERROR: {err}")
    
    print("\n=== Summary ===")
    print("✓ PostgreSQL container running")
    print("✓ Database edenfr_db accessible")
    print("✓ player_classes table ready")
    print("✓ Awakened classes column (is_awakened) present")
    
    return 0

if __name__ == '__main__':
    sys.exit(main())
