#!/usr/bin/env bash
# chain.sh: print the chain for one story: story -> Test Cases -> pull requests -> test methods.
# Read-only. Uses az rest (bearer token from az login, no PAT, no azure-devops extension), gh, jq, grep, awk.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: chain.sh <story-id> [--repo-path DIR] [--pattern ERE]

Print the chain for one User Story:
  story (id, title, state, tags)
  each Test Case linked with Tested By (id, title, state, tags, created date)
  each pull request: the story's Artifact Link relations plus
    gh pr list --search "AB#<id>" in $REPO (the pull request must name AB#<id>)
  the test methods in a checkout that carry a Test Case id

Options:
  --repo-path DIR   Checkout to search for *.cs files. Default: .
  --pattern ERE     Extended regex that marks a Test Case id in code; {id} is
                    replaced by the Test Case id. Default: TC[_-]?{id}
  -h, --help        Show this help and exit.

Environment (all required):
  ADO_ORG       Full organization URL
  ADO_PROJECT   Project name
  REPO          GitHub repository as owner/name

Exit codes: 0 ok, 1 runtime failure, 2 usage error.
EOF
}

die() {
  printf 'chain.sh: %s\n' "$1" >&2
  exit "${2:-1}"
}

story_id=""
repo_path="."
pattern='TC[_-]?{id}'

while [ $# -gt 0 ]; do
  case "$1" in
    -h | --help)
      usage
      exit 0
      ;;
    --repo-path)
      [ $# -ge 2 ] || die "--repo-path needs a directory" 2
      repo_path="$2"
      shift 2
      ;;
    --pattern)
      [ $# -ge 2 ] || die "--pattern needs a regex" 2
      pattern="$2"
      shift 2
      ;;
    -*)
      die "unknown option: $1 (see --help)" 2
      ;;
    *)
      [ -z "$story_id" ] || die "more than one story id given (see --help)" 2
      story_id="$1"
      shift
      ;;
  esac
done

[ -n "$story_id" ] || die "story id missing (see --help)" 2
case "$story_id" in
  *[!0-9]*) die "story id must be a number: $story_id" 2 ;;
esac

for v in ADO_ORG ADO_PROJECT REPO; do
  [ -n "${!v:-}" ] || die "$v is not set. Set ADO_ORG (organization URL), ADO_PROJECT and REPO (owner/name)."
done
for c in az gh jq grep awk; do
  command -v "$c" > /dev/null 2>&1 || die "$c is not installed or not on PATH"
done
[ -d "$repo_path" ] || die "--repo-path is not a directory: $repo_path"

# The Azure DevOps application id is the token audience for az rest.
ado_resource=499b84ac-1321-427f-aa17-267ca6975798
ado_org_url="${ADO_ORG%/}"

work_item() { # work_item <id>: JSON with fields and relations (rel is the reference name)
  az rest --resource "$ado_resource" --only-show-errors -o json \
    --url "$ado_org_url/_apis/wit/workitems/$1?\$expand=relations&api-version=7.1"
}

field() { # field <reference name> <work item json>
  jq -r --arg f "$1" '.fields[$f] // ""' <<< "$2"
}

tags_of() { # tags_of <work item json>: "a; b" becomes "a, b"
  field System.Tags "$1" | sed -e 's/; */, /g'
}

story_json=$(work_item "$story_id") || die "cannot read work item $story_id (az rest failed; check az login and ADO_ORG)"
[ "$(field System.TeamProject "$story_json")" = "$ADO_PROJECT" ] \
  || die "work item $story_id is not in project $ADO_PROJECT"

story_type=$(field System.WorkItemType "$story_json")
story_title=$(field System.Title "$story_json")
story_state=$(field System.State "$story_json")
story_tags=$(tags_of "$story_json")

printf 'Story         AB#%s  %s  %s  [%s]\n              %s\n' \
  "$story_id" "$story_type" "$story_state" "$story_tags" "$story_title"

# Test Cases: Tested By is the Forward side of Microsoft.VSTS.Common.TestedBy on the story.
# The REST API returns the reference name in rel; the display name is accepted too. A work item link
# carries no date, so the Test Case's creation date is printed instead.
tc_links=$(jq -r '
  (.relations // [])[]
  | select(.rel == "Tested By" or .rel == "Microsoft.VSTS.Common.TestedBy-Forward")
  | select(.url | test("/workItems/[0-9]+$"))
  | .url | sub(".*/"; "")' <<< "$story_json")

tc_ids=""
tc_row=""
while read -r tc_id; do
  [ -n "$tc_id" ] || continue
  tc_json=$(work_item "$tc_id") || die "cannot read Test Case $tc_id"
  tc_type=$(field System.WorkItemType "$tc_json")
  [ "$tc_type" = "Test Case" ] || continue
  tc_ids="$tc_ids $tc_id"
  tc_state=$(field System.State "$tc_json")
  tc_tags=$(tags_of "$tc_json")
  printf 'Test Case     TC %s  %s  [%s]\n              %s\n              created %s\n' \
    "$tc_id" "$tc_state" "$tc_tags" "$(field System.Title "$tc_json")" "$(field System.CreatedDate "$tc_json")"
  tc_row="${tc_row:+$tc_row, }TC $tc_id $tc_state [$tc_tags]"
done <<< "$tc_links"
[ -n "$tc_ids" ] || printf 'Test Case     none\n'

# Pull requests. Source 1: Artifact Link relations on the story. A GitHub link ends in
# /pull/<n> and must belong to $REPO. Source 2: gh search for AB#<id>; the hit must name
# AB#<id> as a whole number, because the search tokenizer can match AB#47110 for AB#4711.
linked_prs=$(jq -r --arg repo "$REPO" '
  (.relations // [])[]
  | select(.rel == "Artifact Link" or .rel == "ArtifactLink")
  | .url
  | select(test("/pull/[0-9]+$") and contains("/" + $repo + "/pull/"))
  | sub(".*/pull/"; "")' <<< "$story_json")

azure_prs=$(jq -r '
  (.relations // [])[]
  | select(.rel == "Artifact Link" or .rel == "ArtifactLink")
  | .url
  | select(test("PullRequestId"))
  | sub(".*%2[fF]"; "")' <<< "$story_json")

searched_prs=""
if ! searched_prs=$(gh pr list --repo "$REPO" --state all --search "AB#$story_id" --limit 50 \
  --json number,title,body 2> /dev/null \
  | jq -r --arg id "$story_id" '
      .[] | select(((.title // "") + " " + (.body // "")) | test("AB#" + $id + "([^0-9]|$)"))
      | .number'); then
  printf 'chain.sh: warning: gh pr list failed for %s; pull requests come from story links only\n' "$REPO" >&2
  searched_prs=""
fi

pr_numbers=$(printf '%s\n%s\n' "$linked_prs" "$searched_prs" | grep -E '^[0-9]+$' | sort -n -u || true)
pr_row=""
if [ -n "$pr_numbers" ]; then
  while read -r n; do
    [ -n "$n" ] || continue
    if pr_info=$(gh pr view "$n" --repo "$REPO" --json state,title,isDraft 2> /dev/null); then
      pr_state=$(jq -r 'if .isDraft then "draft" else (.state | ascii_downcase) end' <<< "$pr_info")
      printf 'Pull request  #%s  %s  %s\n' "$n" "$pr_state" "$(jq -r '.title' <<< "$pr_info")"
    else
      printf 'Pull request  #%s  (gh pr view failed)\n' "$n"
    fi
    pr_row="${pr_row:+$pr_row, }PR #$n"
  done <<< "$pr_numbers"
else
  printf 'Pull request  none\n'
fi
if [ -n "$azure_prs" ]; then
  printf 'Pull request  Azure Repos pull request ids on the story, not looked up: %s\n' \
    "$(printf '%s' "$azure_prs" | tr '\n' ' ')"
fi

# Test methods. For each Test Case id, find *.cs files that carry the id. A line that
# carries the id is an attribute or the declaration itself; the method is the next
# declaration within 5 lines. A file whose name carries the id is listed by file name.
test_row=""
for tc_id in $tc_ids; do
  ere="${pattern//\{id\}/$tc_id}"
  ere="(^|[^0-9A-Za-z])${ere}([^0-9]|\$)"
  files=$(grep -rlE --include='*.cs' --exclude-dir=bin --exclude-dir=obj --exclude-dir=.git \
    -e "$ere" "$repo_path" 2> /dev/null || true)
  named=$(find "$repo_path" \( -name bin -o -name obj -o -name .git \) -prune -o -type f -name '*.cs' -print 2> /dev/null \
    | while IFS= read -r p; do printf '%s\n' "$(basename "$p")" | grep -qE "$ere" && printf '%s\n' "$p"; done || true)
  # The harness's own tests name made-up Test Case ids; they never test the product.
  files=$(printf '%s\n%s\n' "$files" "$named" | grep -v '^$' | grep -v '/utils/harness/' | sort -u || true)
  found=""
  while IFS= read -r f; do
    [ -n "$f" ] || continue
    methods=$(awk -v ere="$ere" '
      /(^|[^A-Za-z0-9_])class[ \t]+[A-Za-z0-9_]+/ {
        s = $0
        sub(/.*class[ \t]+/, "", s)
        sub(/[^A-Za-z0-9_].*/, "", s)
        cls = s
      }
      $0 ~ ere && $0 !~ /^[ \t]*(\/\/|\/\*|\*)/ { pending = 6; hit = NR }
      pending > 0 && /^[ \t]*(public|internal|private|protected)[^=;]*\(/ {
        s = $0
        sub(/\(.*/, "", s)
        n = split(s, w, /[ \t]+/)
        print cls "." w[n]
        pending = 0
        next
      }
      pending > 0 { pending-- ; if (pending == 0) print "line " hit }
    ' "$f")
    if [ -n "$methods" ]; then
      while IFS= read -r m; do
        found="${found:+$found, }$m"
      done <<< "$methods"
    fi
    base=$(basename "$f")
    if printf '%s\n' "$base" | grep -qE "$ere"; then
      found="${found:+$found, }$base (file name)"
    fi
  done <<< "$files"
  if [ -n "$found" ]; then
    printf 'Tests         TC %s: %s\n' "$tc_id" "$found"
    test_row="${test_row:+$test_row, }$found"
  else
    printf 'Tests         TC %s: none under %s\n' "$tc_id" "$repo_path"
  fi
done

printf '\n'
printf 'Chain  AB#%s %s [%s] -> %s -> %s -> tests: %s\n' \
  "$story_id" "$story_type" "$story_tags" \
  "${tc_row:-no Test Case}" "${pr_row:-no PR}" "${test_row:-none}"
