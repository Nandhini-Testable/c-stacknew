import json
import sys
from pydriller import Repository

repo_path = sys.argv[1]
out_path = sys.argv[2]
commits = list(Repository(repo_path).traverse_commits())
summary = {
    "commit_count": len(commits),
    "latest": commits[0].hash if commits else None,
}
with open(out_path, "w", encoding="utf-8") as handle:
    json.dump(summary, handle)
print(json.dumps(summary))
