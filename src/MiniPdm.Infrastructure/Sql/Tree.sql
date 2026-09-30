WITH RECURSIVE tree(object_id, version_id, quantity, path, parent_path, is_cycle) AS (
    SELECT o.id, v.id, 1, '/' || o.id || '/', '', 0
    FROM pdm_object o LEFT JOIN current_version v ON v.object_id = o.id
    WHERE o.id = @rootId
    UNION ALL
    SELECT child.id, cv.id, link.quantity, tree.path || child.id || '/', tree.path,
           instr(tree.path, '/' || child.id || '/') > 0
    FROM tree
    JOIN bom_link link ON link.parent_version_id = tree.version_id
    JOIN pdm_object child ON child.id = link.child_object_id
    LEFT JOIN current_version cv ON cv.object_id = child.id
    WHERE tree.is_cycle = 0
)
SELECT o.id ObjectId, o.object_type Type, o.designation Designation,
       COALESCE(v.name, (SELECT name FROM object_version WHERE object_id=o.id ORDER BY version_no DESC LIMIT 1), o.identity) Name,
       v.mass_kg MassKg, tree.version_id VersionId, tree.quantity Quantity,
       tree.path Path, tree.parent_path ParentPath, tree.is_cycle IsCycle
FROM tree JOIN pdm_object o ON o.id = tree.object_id
LEFT JOIN object_version v ON v.id = tree.version_id
ORDER BY length(tree.path), tree.path;
