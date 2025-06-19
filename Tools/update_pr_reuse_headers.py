# update_pr_reuse_headers.py
import subprocess
import os
import sys
from datetime import datetime, timezone
from collections import defaultdict
import argparse
import json
import re

# --- Configuration ---
# Maps PR label names (lowercase) to SPDX License IDs and optional license file paths
# Add new licenses here for future expansion
LICENSE_CONFIG = {
    "mit": {"id": "MIT", "path": "LICENSES/MIT.txt"},
    "agpl": {"id": "AGPL-3.0-or-later", "path": "LICENSES/AGPLv3.txt"},
    # Add more licenses like:
    # "apache-2.0": {"id": "Apache-2.0", "path": "LICENSES/Apache-2.0.txt"},
}
DEFAULT_LICENSE_LABEL = "agpl"

# Dictionary mapping file extensions to comment styles
# Format: {extension: (prefix, suffix)}
# If suffix is None, it's a single-line comment style
COMMENT_STYLES = {
    ".cs": ("//", None),
    ".js": ("//", None),
    ".yaml": ("#", None),
    ".yml": ("#", None),
    ".ftl": ("#", None),
    ".py": ("#", None),
    ".sh": ("#", None),
    ".ps1": ("#", None),
    ".bat": ("REM", None),
    ".xaml": ("<!--", "-->"),
    ".xml": ("<!--", "-->"),
}
REPO_PATH = "."

def run_git_command(command, cwd=REPO_PATH, check=True):
    """Runs a git command and returns its output."""
    try:
        result = subprocess.run(
            command,
            capture_output=True,
            text=True,
            check=check,
            cwd=cwd,
            encoding='utf-8',
            errors='ignore'
        )
        return result.stdout.strip()
    except subprocess.CalledProcessError as e:
        # Don't print error if check=False and it returns non-zero, git log might return empty
        if check:
            print(f"Error running git command {' '.join(command)}: {e.stderr}", file=sys.stderr)
        return None
    except FileNotFoundError:
        print("FATAL: 'git' command not found. Make sure git is installed and in your PATH.", file=sys.stderr)
        return None

def get_pr_authors_for_file(file_path, base_sha, head_sha, cwd=REPO_PATH):
    """
    Gets authors (including co-authors) and their contribution years *within the PR's commit range*
    for a specific file.
    Returns: (dict like {"Author Name <email>": (min_year, max_year)}, list_of_warnings)
    """
    commit_range = f"{base_sha}..{head_sha}"
    command = ["git", "log", commit_range, "--pretty=format:%at%x00%an%x00%ae%x00%b%x1E", "--", file_path]
    co_author_regex = re.compile(r"^Co-authored-by:\s*(.*?)\s*<([^>]+)>", re.IGNORECASE | re.MULTILINE)

    output = run_git_command(command, cwd=cwd, check=False)
    author_timestamps = defaultdict(list)
    warnings = []

    if output is None or not output.strip():
        return {}, warnings

    commits = output.strip().split('\x1E')

    for commit_data in commits:
        if not commit_data.strip():
            continue

        parts = commit_data.strip().split('\x00')
        if len(parts) < 4:
            warnings.append(f"Skipping malformed commit data for {file_path}: {commit_data[:100]}...")
            continue

        timestamp_str, author_name, author_email, body = parts[0], parts[1], parts[2], parts[3]

        try:
            ts_int = int(timestamp_str)
        except ValueError:
            warnings.append(f"Skipping invalid timestamp for {file_path}: {timestamp_str}")
            continue

        # Process main author
        if author_name and author_email:
            author_key = f"{author_name.strip()} <{author_email.strip()}>"
            author_timestamps[author_key].append(ts_int)
        else:
            warnings.append(f"Skipping commit with missing author info for {file_path}: Name='{author_name}', Email='{author_email}'")

        # Process co-authors
        for match in co_author_regex.finditer(body):
            co_author_name = match.group(1).strip()
            co_author_email = match.group(2).strip()
            if co_author_name and co_author_email:
                co_author_key = f"{co_author_name} <{co_author_email}>"
                author_timestamps[co_author_key].append(ts_int)
            else:
                 warnings.append(f"Skipping malformed Co-authored-by line in commit body for {file_path}: Name='{co_author_name}', Email='{co_author_email}'")

    author_years = {}
    for author, timestamps in author_timestamps.items():
        if not timestamps: continue
        try:
            min_ts = min(timestamps)
            max_ts = max(timestamps)
            min_year = datetime.fromtimestamp(min_ts, timezone.utc).year
            max_year = datetime.fromtimestamp(max_ts, timezone.utc).year
            author_years[author] = (min_year, max_year)
        except Exception as e:
             warnings.append(f"Error calculating year range for author {author} on file {file_path}: {e}")

    return author_years, warnings

def get_all_authors_for_file(file_path, cwd=REPO_PATH):
    """
    Gets *all* historical authors (including co-authors) and their contribution years
    for a specific file.
    Returns: (dict like {"Author Name <email>": (min_year, max_year)}, list_of_warnings)
    """
    command = ["git", "log", "--pretty=format:%at%x00%an%x00%ae%x00%b%x1E", "--follow", "--", file_path]
    co_author_regex = re.compile(r"^Co-authored-by:\s*(.*?)\s*<([^>]+)>", re.IGNORECASE | re.MULTILINE)

    output = run_git_command(command, cwd=cwd, check=False)
    author_timestamps = defaultdict(list)
    warnings = []

    if output is None or not output.strip():
        return {}, warnings

    commits = output.strip().split('\x1E')

    for commit_data in commits:
        if not commit_data.strip():
            continue

        parts = commit_data.strip().split('\x00')
        if len(parts) < 4:
            warnings.append(f"Skipping malformed commit data for {file_path}: {commit_data[:100]}...")
            continue

        timestamp_str, author_name, author_email, body = parts[0], parts[1], parts[2], parts[3]

        try:
            ts_int = int(timestamp_str)
        except ValueError:
            warnings.append(f"Skipping invalid timestamp for {file_path}: {timestamp_str}")
            continue

        # Process main author
        if author_name and author_email:
            author_key = f"{author_name.strip()} <{author_email.strip()}>"
            author_timestamps[author_key].append(ts_int)
        else:
            warnings.append(f"Skipping commit with missing author info for {file_path}: Name='{author_name}', Email='{author_email}'")

        # Process co-authors
        for match in co_author_regex.finditer(body):
            co_author_name = match.group(1).strip()
            co_author_email = match.group(2).strip()
            if co_author_name and co_author_email:
                co_author_key = f"{co_author_name} <{co_author_email}>"
                author_timestamps[co_author_key].append(ts_int)
            else:
                 warnings.append(f"Skipping malformed Co-authored-by line in commit body for {file_path}: Name='{co_author_name}', Email='{co_author_email}'")

    author_years = {}
    for author, timestamps in author_timestamps.items():
        if not timestamps: continue
        try:
            min_ts = min(timestamps)
            max_ts = max(timestamps)
            min_year = datetime.fromtimestamp(min_ts, timezone.utc).year
            max_year = datetime.fromtimestamp(max_ts, timezone.utc).year
            author_years[author] = (min_year, max_year)
        except Exception as e:
             warnings.append(f"Error calculating year range for author {author} on file {file_path}: {e}")

    return author_years, warnings


def parse_existing_header(content, comment_style):
    """
    Parses an existing REUSE header to extract authors and license.
    Returns: (authors_dict, license_id, header_lines)

    comment_style is a tuple of (prefix, suffix)
    """
    prefix, suffix = comment_style
    lines = content.splitlines()
    authors = {}
    license_id = None
    header_lines = []

    if suffix is None:
        # Single-line comment style (e.g., //, #)
        # Regular expressions for parsing
        copyright_regex = re.compile(f"^{re.escape(prefix)} SPDX-FileCopyrightText: (\\d{{4}}) (.+)$")
        license_regex = re.compile(f"^{re.escape(prefix)} SPDX-License-Identifier: (.+)$")

        # Find the header section
        in_header = True
        for i, line in enumerate(lines):
            if in_header:
                header_lines.append(line)

                # Check for copyright line
                copyright_match = copyright_regex.match(line)
                if copyright_match:
                    year = int(copyright_match.group(1))
                    author = copyright_match.group(2).strip()
                    authors[author] = (year, year)
                    continue

                # Check for license line
                license_match = license_regex.match(line)
                if license_match:
                    license_id = license_match.group(1).strip()
                    continue

                # Empty comment line or separator
                if line.strip() == prefix:
                    continue

                # If we get here, we've reached the end of the header
                if i > 0:  # Only if we've processed at least one line
                    header_lines.pop()  # Remove the non-header line
                    in_header = False
            else:
                break
    else:
        # Multi-line comment style (e.g., <!-- -->)
        # Regular expressions for parsing
        copyright_regex = re.compile(r"^SPDX-FileCopyrightText: (\d{4}) (.+)$")
        license_regex = re.compile(r"^SPDX-License-Identifier: (.+)$")

        # Find the header section
        in_comment = False
        for i, line in enumerate(lines):
            stripped_line = line.strip()

            # Start of comment
            if stripped_line == prefix:
                in_comment = True
                header_lines.append(line)
                continue

            # End of comment
            if stripped_line == suffix and in_comment:
                header_lines.append(line)
                break

            if in_comment:
                header_lines.append(line)

                # Check for copyright line
                copyright_match = copyright_regex.match(stripped_line)
                if copyright_match:
                    year = int(copyright_match.group(1))
                    author = copyright_match.group(2).strip()
                    authors[author] = (year, year)
                    continue

                # Check for license line
                license_match = license_regex.match(stripped_line)
                if license_match:
                    license_id = license_match.group(1).strip()
                    continue

    return "\n".join(cleaned_lines[first_content_line_index:]) if cleaned_lines else ""


def create_header(authors, license_id, comment_style):
    """
    Creates a REUSE header with the given authors and license.
    Returns: header string

    comment_style is a tuple of (prefix, suffix)
    """
    prefix, suffix = comment_style
    lines = []

    if suffix is None:
        # Single-line comment style (e.g., //, #)
        # Add copyright lines
        if authors:
            for author, (_, year) in sorted(authors.items(), key=lambda x: (x[1][1], x[0])):
                if not author.startswith("Unknown <"):
                    lines.append(f"{prefix} SPDX-FileCopyrightText: {year} {author}")
        else:
            lines.append(f"{prefix} SPDX-FileCopyrightText: Contributors to the GoobStation14 project")

        # Add separator
        lines.append(f"{prefix}")

        # Add license line
        lines.append(f"{prefix} SPDX-License-Identifier: {license_id}")
    else:
        # Multi-line comment style (e.g., <!-- -->)
        # Start comment
        lines.append(f"{prefix}")

        # Add copyright lines
        if authors:
            for author, (_, year) in sorted(authors.items(), key=lambda x: (x[1][1], x[0])):
                if not author.startswith("Unknown <"):
                    lines.append(f"SPDX-FileCopyrightText: {year} {author}")
        else:
            lines.append(f"SPDX-FileCopyrightText: Contributors to the GoobStation14 project")

        # Add separator
        lines.append("")

        # Add license line
        lines.append(f"SPDX-License-Identifier: {license_id}")

        # End comment
        lines.append(f"{suffix}")

    return "\n".join(lines)

def process_file(file_path, default_license_id):
    """
    Processes a file to add or update REUSE headers.
    Returns: True if file was modified, False otherwise
    """
    # Check file extension
    _, ext = os.path.splitext(file_path)
    comment_style = COMMENT_STYLES.get(ext)
    if not comment_style:
        print(f"Skipping unsupported file type: {file_path}")
        return False

    full_file_path = os.path.join(REPO_PATH, file_path)
    if not os.path.exists(full_file_path):
         print(f"  Skipping (file not found): {file_path}", file=sys.stderr)
         return False # Should not happen in workflow context

    # Get authors only from the PR commits for new files
    author_years, warnings = get_pr_authors_for_file(file_path, base_sha, head_sha, REPO_PATH)
    if warnings:
        for warn in warnings: print(f"  Warning: {warn}", file=sys.stderr)

    reuse_header = create_reuse_header(author_years, license_id, comment_prefix)

    try:
        with open(full_file_path, 'r', encoding='utf-8-sig', errors='ignore') as f:
            original_content = f.read()

        # Check if file *already* has a header (e.g., user added it manually)
        existing_license = extract_license_identifier(original_content, comment_prefix)
        if existing_license:
            print(f"  Skipping (already has header with license {existing_license}): {file_path}")
            return False

        cleaned_content = remove_existing_reuse_header(original_content, comment_prefix) # Should ideally do nothing if no header
        separator = "\n\n" if cleaned_content.strip() else "" # Add extra newline for new files

        # Handle shebangs or initial comments for YAML
        if comment_prefix == '#' and cleaned_content.startswith('#'):
             new_content = reuse_header + "\n" + cleaned_content
        else:
             new_content = reuse_header + separator + cleaned_content

        final_content_lf = new_content.replace('\r\n', '\n').replace('\r', '\n')

        with open(full_file_path, 'w', encoding='utf-8', newline='\n') as f:
            f.write(final_content_lf)
        print(f"  ADDED header to {file_path}")
        return True
    except Exception as e:
        print(f"  Error processing file {file_path}: {e}", file=sys.stderr)
        return False

    # Read file content
    with open(full_path, 'r', encoding='utf-8-sig', errors='ignore') as f:
        content = f.read()

    # Parse existing header if any
    existing_authors, existing_license, header_lines = parse_existing_header(content, comment_style)

    # Get all authors from git
    git_authors = get_authors_from_git(file_path)

    # Add current user to authors
    try:
        name_cmd = ["git", "config", "user.name"]
        email_cmd = ["git", "config", "user.email"]
        user_name = run_git_command(name_cmd, check=False)
        user_email = run_git_command(email_cmd, check=False)

        if user_name and user_email and user_name.strip() != "Unknown":
            # Use current year
            current_year = datetime.now(timezone.utc).year
            current_user = f"{user_name} <{user_email}>"

            # Add current user if not already present
            if current_user not in git_authors:
                git_authors[current_user] = (current_year, current_year)
                print(f"  Added current user: {current_user}")
            else:
                # Update year if necessary
                min_year, max_year = git_authors[current_user]
                git_authors[current_user] = (min(min_year, current_year), max(max_year, current_year))
        else:
            print("Warning: Could not get current user from git config or name is 'Unknown'")
    except Exception as e:
        print(f"Error getting git user: {e}")

    # Determine what to do based on existing header
    if existing_license:
        print(f"Updating existing header for {file_path} (License: {existing_license})")

        # Combine existing and git authors
        combined_authors = existing_authors.copy()
        for author, (git_min, git_max) in git_authors.items():
            if author.startswith("Unknown <"):
                continue
            if author in combined_authors:
                existing_min, existing_max = combined_authors[author]
                combined_authors[author] = (min(existing_min, git_min), max(existing_max, git_max))
            else:
                combined_authors[author] = (git_min, git_max)
                print(f"  Adding new author: {author}")

        # Create new header with existing license
        new_header = create_header(combined_authors, existing_license, comment_style)

        # Replace old header with new header
        if header_lines:
            old_header = "\n".join(header_lines)
            new_content = content.replace(old_header, new_header, 1)
        else:
            # No header found (shouldn't happen if existing_license is set)
            new_content = new_header + "\n\n" + content
    else:
        print(f"Adding new header to {file_path} (License: {default_license_id})")

        # Create new header with default license
        new_header = create_header(git_authors, default_license_id, comment_style)

        # Add header to file
        if content.strip():
            # For XML files, we need to add the header after the XML declaration if present
            prefix, suffix = comment_style
            if suffix and content.lstrip().startswith("<?xml"):
                # Find the end of the XML declaration
                xml_decl_end = content.find("?>") + 2
                xml_declaration = content[:xml_decl_end]
                rest_of_content = content[xml_decl_end:].lstrip()
                new_content = xml_declaration + "\n" + new_header + "\n\n" + rest_of_content
            else:
                new_content = new_header + "\n\n" + content
        else:
            print(f"  Skipping (no changes needed): {file_path}")
            return False

    except Exception as e:
        print(f"  Error processing file {file_path}: {e}", file=sys.stderr)
        return False

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Update REUSE headers for PR.")
    parser.add_argument("--files-added", nargs='*', default=[], help="List of added files.")
    parser.add_argument("--files-modified", nargs='*', default=[], help="List of modified files.")
    parser.add_argument("--pr-license", default=DEFAULT_LICENSE_LABEL, help="License to use for new files (mit or agpl).")
    parser.add_argument("--pr-base-sha", required=True, help="Base SHA of the PR.")
    parser.add_argument("--pr-head-sha", required=True, help="Head SHA of the PR.")

    args = parser.parse_args()

    print("Starting REUSE header update for PR...")
    print(f"Base SHA: {args.pr_base_sha}")
    print(f"Head SHA: {args.pr_head_sha}")

    # Determine license for new files
    new_file_license_label = args.pr_license.lower()
    if new_file_license_label not in LICENSE_CONFIG:
        print(f"  Warning: Unrecognized license '{new_file_license_label}', using default: {DEFAULT_LICENSE_LABEL}", file=sys.stderr)
        new_file_license_label = DEFAULT_LICENSE_LABEL

    print(f"Using license for new files: {new_file_license_label}")

    new_file_license_id = LICENSE_CONFIG.get(new_file_license_label, {}).get("id")
    if not new_file_license_id:
        print(f"FATAL: Could not find SPDX ID for license label '{new_file_license_label}'. Check LICENSE_CONFIG.", file=sys.stderr)
        sys.exit(1)

    print(f"License ID for NEW files: {new_file_license_id}")

    files_changed = False

    # Process Added Files
    print("\n--- Processing Added Files ---")
    for file in args.files_added:
        if process_added_file(file, new_file_license_id, args.pr_base_sha, args.pr_head_sha):
            files_changed = True

    # Process Modified Files
    print("\n--- Processing Modified Files ---")
    for file in args.files_modified:
        if process_modified_file(file, args.pr_base_sha, args.pr_head_sha):
            files_changed = True

    print("\n--- Summary ---")
    if files_changed:
        print("Script finished: Files were modified.")
    else:
        print("Script finished: No files needed changes.")

    print("----------------")
