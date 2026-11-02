"""Check API Gateway metadata for the failures found during the first deploy."""
import json
from pathlib import Path


def validate_routes(routes):
    variable_children = {}
    for route, metadata in routes.items():
        cors = metadata.get("enable_cors")
        if not isinstance(cors, dict) or not isinstance(cors.get("state"), bool):
            raise ValueError(f"{route}: enable_cors.state must be a boolean")
        parent = []
        for part in route.strip("/").split("/"):
            if part.startswith("{"):
                parent_key = tuple(parent)
                previous = variable_children.setdefault(parent_key, part)
                if previous != part:
                    raise ValueError(f"{route}: conflicting parameters {previous} and {part}")
                parent.append("{}")
            else:
                parent.append(part)


if __name__ == "__main__":
    root = Path(__file__).resolve().parent.parent
    metadata_path = root / "backend/restaurant-backend-app/deployment_resources.json"
    resources = json.loads(metadata_path.read_text(encoding="utf-8-sig"))
    for resource in resources.values():
        if resource.get("resource_type") == "api_gateway":
            routes = resource["resources"]
            validate_routes(routes)
            print(f"Validated {len(routes)} API routes: CORS and sibling parameter names.")
