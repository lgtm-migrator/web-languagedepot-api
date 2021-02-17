import { missingRequiredParam } from '$utils/commonErrors';
import { addUserWithRoleByProjectCode } from '$utils/db/usersAndRoles';
import { dbs } from '$db/dbsetup';
import { allowManagerOrAdmin } from '$utils/db/authRules';

export async function post({ params, path, query, headers }) {
    if (!params.projectCode) {
        return missingRequiredParam('projectCode', path);
    }
    if (!params.username) {
        return missingRequiredParam('username', path);
    }
    if (!params.rolename) {
        return missingRequiredParam('rolename', path);
    }
    const db = query.private ? dbs.private : dbs.public;
    const authResult = await allowManagerOrAdmin(db, { params, headers });
    if (authResult.status === 200) {
        return addUserWithRoleByProjectCode(db, params.projectCode, params.username, params.rolename);
    } else {
        return authResult;
    }
}
