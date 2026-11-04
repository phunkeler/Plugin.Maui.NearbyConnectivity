#!/usr/bin/env bash
# measure.sh: the three morning numbers, from Boards (WIQL, revisions) and GitHub (gh). Read-only.
# Row names are the measurement documents' names; two rows are proxies and say so.
# Uses az rest (bearer token from az login, no PAT, no azure-devops extension), gh, jq.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: measure.sh [--days N]

Print three numbers for the User Stories closed in the last N days (default 30):

  Undeclared changes (proxy)
                     stories closed with no needs-qa tag and no Tested By link to a
                     Test Case / all closed stories. The documents' measure counts merged
                     pull requests whose diff reaches a device; az and gh alone cannot
                     read reach, so closed stories stand in for it.
  Test Case Ready before the PR opens
                     linked Test Cases whose Ready date is earlier than the open date of
                     the story's pull request (earliest, when several) / all Test Cases
                     linked to those stories. A Test Case never in Ready counts as not.
  Merge to close (proxy for merge to verified; close unfiltered)
                     median days from the pull request merge to the story Closed date.
                     Verified needs a Test Case result on a build with the merge; this
                     reads the story's close instead, whoever closed it and why.

Definitions used:
  Closed             System.State in (Closed, Done) and Microsoft.VSTS.Common.ClosedDate
                     inside the window.
  Story's PR         Merged pull requests in $REPO whose title or body names AB#<id>.
                     Merge to close uses the latest merge; a story closed before that
                     merge is left out and counted in the detail column.
  Ready date         System.ChangedDate of the first revision of the Test Case whose
                     System.State is Ready (GET _apis/wit/workitems/{id}/revisions).
                     A link carries no date, so the link is not timed.

Options:
  --days N     Window in days. Default: 30.
  -h, --help   Show this help and exit.

Environment (all required):
  ADO_ORG       Full organization URL
  ADO_PROJECT   Project name
  REPO          GitHub repository as owner/name

Exit codes: 0 ok, 1 runtime failure, 2 usage error.
EOF
}

die() {
  printf 'measure.sh: %s\n' "$1" >&2
  exit "${2:-1}"
}

days=30
while [ $# -gt 0 ]; do
  case "$1" in
    -h | --help)
      usage
      exit 0
      ;;
    --days)
      [ $# -ge 2 ] || die "--days needs a number" 2
      days="$2"
      shift 2
      ;;
    *)
      die "unknown argument: $1 (see --help)" 2
      ;;
  esac
done
case "$days" in
  '' | *[!0-9]*) die "--days must be a positive integer: $days" 2 ;;
esac
[ "$days" -ge 1 ] || die "--days must be at least 1" 2

for v in ADO_ORG ADO_PROJECT REPO; do
  [ -n "${!v:-}" ] || die "$v is not set. Set ADO_ORG (organization URL), ADO_PROJECT and REPO (owner/name)."
done
for c in az gh jq; do
  command -v "$c" > /dev/null 2>&1 || die "$c is not installed or not on PATH"
done

# jq helpers shared by the programs below. ts turns an ISO 8601 time into epoch seconds;
# fromdateiso8601 rejects fractional seconds, so they are dropped first.
jq_defs='def ts: sub("\\.[0-9]+"; "") | fromdateiso8601;'

# 1. Closed stories in the window. One WIQL call for the ids.
# The window start is computed here, as a date literal: GNU date first, then BSD date.
since=$(date -u -d "$days days ago" +%F 2> /dev/null || date -u -v-"${days}"d +%F) \
  || die "cannot compute the window start with date"
q="'"
project_literal="${ADO_PROJECT//$q/$q$q}"
wiql="SELECT [System.Id], [System.Tags], [Microsoft.VSTS.Common.ClosedDate] FROM WorkItems"
wiql="$wiql WHERE [System.TeamProject] = '$project_literal'"
wiql="$wiql AND [System.WorkItemType] = 'User Story'"
wiql="$wiql AND [System.State] IN ('Closed', 'Done')"
wiql="$wiql AND [Microsoft.VSTS.Common.ClosedDate] >= '$since'"

ado_resource=499b84ac-1321-427f-aa17-267ca6975798
ado_org_url="${ADO_ORG%/}"
project_path=$(printf '%s' "$ADO_PROJECT" | sed 's/ /%20/g')

rest_get() { # rest_get <url>
  az rest --resource "$ado_resource" --only-show-errors -o json --url "$1"
}
rest_post() { # rest_post <url> <json body>
  az rest --resource "$ado_resource" --only-show-errors -o json --method post --url "$1" --body "$2"
}

# WIQL returns ids only. Fields and relations come from the batch endpoint, 200 ids per call.
# The batch endpoint takes either a field list or $expand, not both; $expand returns all fields.
wiql_out=$(rest_post "$ado_org_url/$project_path/_apis/wit/wiql?api-version=7.1" \
  "$(jq -nc --arg q "$wiql" '{query: $q}')") \
  || die "WIQL query failed (are you signed in? try az login; check ADO_ORG and ADO_PROJECT)"
ids=$(jq -c '[(.workItems // [])[].id]' <<< "$wiql_out")
closed='[]'
offset=0
count=$(jq 'length' <<< "$ids")
while [ "$offset" -lt "$count" ]; do
  chunk=$(jq -c --argjson o "$offset" '.[$o:$o + 200]' <<< "$ids")
  body=$(jq -nc --argjson ids "$chunk" \
    '{ids: $ids, "$expand": "Relations"}')
  part=$(rest_post "$ado_org_url/_apis/wit/workitemsbatch?api-version=7.1" "$body") \
    || die "work item batch read failed"
  closed=$(jq -c --argjson p "$(jq -c '.value' <<< "$part")" '. + $p' <<< "$closed")
  offset=$((offset + 200))
done

total=$(jq 'length' <<< "$closed")
# Undeclared (proxy): no needs-qa tag and no Tested By link. A Tested By link is taken to point at
# a Test Case; reading each target's type would cost one call per link.
undeclared=$(jq '[.[] | select(
    ((.fields["System.Tags"] // "" | ascii_downcase | split(";")
      | map(gsub("^\\s+|\\s+$"; "")) | index("needs-qa")) == null)
    and ([(.relations // [])[]
          | select(.rel == "Tested By" or .rel == "Microsoft.VSTS.Common.TestedBy-Forward")] | length) == 0
  )] | length' <<< "$closed")

# 2. Merged pull requests that name AB#<id>. One gh call for the whole repository, newest
# first, joined to the stories in jq. A search per story would hit the search rate limit.
# The limit is the most gh returns in one call; older merges are not seen, so a story
# whose pull request is older than the newest 1000 merges counts as having no pull request.
pr_limit=1000
prs_file=$(mktemp)
trap 'rm -f "$prs_file"' EXIT
gh pr list --repo "$REPO" --state merged --limit "$pr_limit" \
  --json number,title,body,createdAt,mergedAt > "$prs_file" \
  || die "gh pr list failed for $REPO (run gh auth status)"

# One row per closed story that has a merged pull request:
# { id, closed, merged (latest), opened (earliest) } in epoch seconds.
rows=$(jq -c --slurpfile prs "$prs_file" "$jq_defs"'
  ($prs[0] | map(
      . as $p
      | (((($p.title // "") + " " + ($p.body // "")) | [scan("AB#([0-9]+)") | .[0] | tonumber] | unique)[]) as $s
      | { story: $s, opened: ($p.createdAt | ts), merged: ($p.mergedAt | ts) })) as $links
  | [ .[]
      | { id: .id, closed: (.fields["Microsoft.VSTS.Common.ClosedDate"] | ts) } as $c
      | ($links | map(select(.story == $c.id))) as $l
      | select(($l | length) > 0)
      | { id: $c.id, closed: $c.closed,
          merged: ($l | map(.merged) | max), opened: ($l | map(.opened) | min) } ]' <<< "$closed")

with_pr=$(jq 'length' <<< "$rows")
# Median of (closed - merged) in days, only where the story closed at or after the merge.
mq=$(jq -r '
  [ .[] | (.closed - .merged) | select(. >= 0) | . / 86400 ] | sort as $d
  | ($d | length) as $n
  | if $n == 0 then "none 0"
    else (if $n % 2 == 1 then $d[($n - 1) / 2] else (($d[$n / 2 - 1] + $d[$n / 2]) / 2) end) as $m
         | "\($m * 10 | round / 10) \($n)"
    end' <<< "$rows")
median=${mq% *}
median_n=${mq#* }
left_out=$((with_pr - median_n))

# 3. Test Case Ready before the PR opens. A link has no date (its attributes hold only isLocked
# and name), so the clock is the Test Case's own state history: one revisions GET per linked
# Test Case of a story that has a pull request, compared with the earliest pull request open date.
tc_total=0
tc_before=0
if [ "$with_pr" -gt 0 ]; then
  while read -r id opened; do
    [ -n "$id" ] || continue
    for tc in $(jq -r --argjson id "$id" '.[] | select(.id == $id) | (.relations // [])[]
        | select(.rel == "Tested By" or .rel == "Microsoft.VSTS.Common.TestedBy-Forward")
        | .url | sub(".*/"; "")' <<< "$closed"); do
      revs=$(rest_get "$ado_org_url/_apis/wit/workitems/$tc/revisions?api-version=7.1") \
        || die "cannot read the revisions of work item $tc"
      before=$(jq -r --argjson opened "$opened" "$jq_defs"'
        [ .value | sort_by(.rev)[] | select(.fields["System.State"] == "Ready") ] | first
        | if . == null then 0 elif (.fields["System.ChangedDate"] | ts) < $opened then 1 else 0 end' <<< "$revs")
      tc_total=$((tc_total + 1))
      tc_before=$((tc_before + before))
    done
  done < <(jq -r '.[] | "\(.id) \(.opened)"' <<< "$rows")
fi

pct() { # pct <part> <whole>
  if [ "$2" -eq 0 ]; then printf 'n/a'; else awk -v a="$1" -v b="$2" 'BEGIN { printf "%.0f%%", 100 * a / b }'; fi
}

undeclared_value="$undeclared/$total ($(pct "$undeclared" "$total"))"
if [ "$median_n" -eq 0 ]; then median_value="n/a"; else median_value="$median days"; fi
timing_value="$tc_before/$tc_total ($(pct "$tc_before" "$tc_total"))"

printf 'Window: last %s days. Project: %s. Repository: %s.\n\n' "$days" "$ADO_PROJECT" "$REPO"
row() { printf '%s\n  %-16s %s\n' "$1" "$2" "$3"; }
row "Undeclared changes (proxy)" "$undeclared_value" \
  "stories closed with no needs-qa and no Test Case / closed stories"
row "Test Case Ready before the PR opens" "$timing_value" \
  "linked Test Cases in Ready before the pull request opened / all linked Test Cases on those stories"
row "Merge to close (proxy for merge to verified; close unfiltered)" "$median_value" \
  "median over $median_n of $with_pr closed stories with a merged AB# pull request ($left_out closed before the merge)"
