module Model

open System
open System.Linq
open FSharp.Data.Sql
open Shared
open MySql.Data.MySqlClient

[<Literal>]
let sampleConnString = "Server=localhost;Database=testldapi;User=rmunn"

[<Literal>]
let resolutionPath = __SOURCE_DIRECTORY__

[<Literal>]
let schemaPath = __SOURCE_DIRECTORY__ + "/languagedepot.schema"

type sql = SqlDataProvider<Common.DatabaseProviderTypes.MYSQL,
                           sampleConnString,
                        //    Owner = "languagedepot",
                           ContextSchemaPath = schemaPath,
                           ResolutionPath = resolutionPath,
                           CaseSensitivityChange = Common.CaseSensitivityChange.ORIGINAL,
                           UseOptionTypes = true>

// TODO: Add "is_archived" boolean to model (default false) so we can implement archiving; update queries that list or count projects to specify "where (isArchived = false)"
type Dto.ProjectDetails with
    static member FromSql (sqlProject : sql.dataContext.``languagedepot.projectsEntity``) = {
        Dto.ProjectDetails.code = sqlProject.Identifier |> Option.defaultWith (fun _ -> sqlProject.Name.ToLowerInvariant().Replace(" ", "_"))
        Dto.ProjectDetails.name = sqlProject.Name
        Dto.ProjectDetails.description = sqlProject.Description |> Option.defaultValue ""
        Dto.ProjectDetails.membership = None
    }
    static member FromSqlWithRoles (sqlProjectAndRoles : (sql.dataContext.``languagedepot.projectsEntity`` * string * int * string) list) =
        match sqlProjectAndRoles |> List.tryHead with
        | None -> None
        | Some (sqlProject, _, _, _) ->
            let memberships = sqlProjectAndRoles |> List.choose (fun (_, username, roleId, roleName) ->
                if String.IsNullOrEmpty username || roleId = 0 || String.IsNullOrEmpty roleName
                then None
                else
                    match RoleType.TryOfString roleName with
                    | Some role -> Some (username, role)
                    | None -> None)
            { Dto.ProjectDetails.FromSql sqlProject with membership = Some memberships } |> Some

type Dto.UserDetails with
    static member FromSql ((login, firstname, lastname, language, email) : (string * string * string * string option * string option)) =
        {
            Dto.UserDetails.username = login
            Dto.UserDetails.firstName = firstname
            Dto.UserDetails.lastName = lastname
            Dto.UserDetails.email = email
            Dto.UserDetails.language = language |> Option.defaultValue "en"
        }

type Dto.RoleDetails with
    static member FromSql (sqlRole : sql.dataContext.``languagedepot.rolesEntity``) = {
        Dto.RoleDetails.name = sqlRole.Name
        Dto.RoleDetails.``type`` = RoleType.OfString sqlRole.Name
    }
    static member TypeFromSql (sqlRole : sql.dataContext.``languagedepot.rolesEntity``) = RoleType.OfString sqlRole.Name

type ListUsers = string -> int option -> int option -> Async<Dto.UserDetails list>
type ListProjects = string -> Async<Dto.ProjectList>
// These three CountFoo types all look the same, so we have to use a single-case DU to distinguish them
type CountUsers = CountUsers of (string -> Async<int>)
type CountProjects = CountProjects of (string -> Async<int>)
type CountRealProjects = CountRealProjects of (string -> Async<int>)
type ListRoles = string -> Async<Dto.RoleDetails list>
type ProjectsByUser = string -> string -> Async<Dto.ProjectDetails list>
type ProjectsByUserRole = string -> string -> RoleType -> Async<Dto.ProjectDetails list>
type ProjectsAndRolesByUser = string -> string -> Async<(Dto.ProjectDetails * RoleType list) list>
type ProjectsAndRolesByUserRole = string -> string -> RoleType -> Async<(Dto.ProjectDetails * RoleType list) list>
// Ditto for these two FooExists types: need a DU
type UserExists = UserExists of (string -> string -> Async<bool>)
type ProjectExists = ProjectExists of (string -> string -> Async<bool>)
type IsAdmin = IsAdmin of (string -> string -> Async<bool>)
type SearchUsersExact = SearchUsersExact of (string -> string -> Async<Dto.UserList>)
type SearchUsersLoose = SearchUsersLoose of (string -> string -> Async<Dto.UserList>)
type GetUser = string -> string -> Async<Dto.UserDetails option>
type GetProject = string -> string -> Async<Dto.ProjectDetails option>
type CreateProject = string -> Api.CreateProject -> Async<int>
type CreateUser = string -> Api.CreateUser -> Async<int>
type UpsertUser = string -> string -> Api.CreateUser -> Async<int>
type ChangePassword = string -> string -> Api.ChangePassword -> Async<bool>
type VerifyLoginCredentials = string -> Api.LoginCredentials -> Async<bool>
type AddMembership = AddMembership of (string -> string -> string -> RoleType -> Async<bool>)
type RemoveMembership = RemoveMembership of (string -> string -> string -> RoleType -> Async<bool>)
type RemoveUserFromAllRolesInProject = RemoveUserFromAllRolesInProject of (string -> string -> string -> Async<bool>)
type ArchiveProject = string -> string -> Async<bool>

open FSharp.Control.Tasks.V2

let getSqlResult (convertRow : MySqlDataReader -> 'result) (reader : MySqlDataReader) = task {
    if not reader.HasRows then
        return Array.empty
    else
        let result = new ResizeArray<'result>()
        let mutable hasMore = true
        let! readResult = reader.ReadAsync()
        hasMore <- readResult
        while hasMore do
            let item = convertRow reader
            result.Add item
            let! readResult = reader.ReadAsync()
            hasMore <- readResult
        return result.ToArray()
}

let fetchDataWithParams (connString : string) (sql : string) (setParams : MySqlCommand -> unit) (convertRow : MySqlDataReader -> 'result) = task {
    use conn = new MySqlConnection(connString)
    do! conn.OpenAsync()
    use cmd = new MySqlCommand(sql, conn)
    setParams cmd
    use! reader = cmd.ExecuteReaderAsync()
    let! result = reader :?> MySqlDataReader |> getSqlResult convertRow
    return result
}

let fetchData (connString : string) (sql : string) (convertRow : MySqlDataReader -> 'result) =
    fetchDataWithParams connString sql ignore convertRow

let doScalarQueryWithParams<'result> (connString : string) (sql : string) (setParams : MySqlCommand -> unit) = task {
    use conn = new MySqlConnection(connString)
    do! conn.OpenAsync()
    use cmd = new MySqlCommand(sql, conn)
    setParams cmd
    let! boxedResult = cmd.ExecuteScalarAsync()
    return (unbox<'result> boxedResult)
}

let doScalarQuery<'result> connString sql =
    doScalarQueryWithParams<'result> connString sql ignore

let doCountQueryWithParams connString sql setParams = doScalarQueryWithParams<int> connString sql setParams
let doCountQuery connString sql = doCountQueryWithParams connString sql ignore

let doNonQueryWithParams (connString : string) (sql : string) (setParams : MySqlCommand -> unit) = task {
    use conn = new MySqlConnection(connString)
    do! conn.OpenAsync()
    use cmd = new MySqlCommand(sql, conn)
    setParams cmd
    let! result = cmd.ExecuteNonQueryAsync()
    try
        let count = unbox<int> result
        return count
    with _ ->
        return 0
}

let doNonQuery (connString : string) (sql : string) =
    doNonQueryWithParams connString sql ignore

let convertUserRow (reader : MySqlDataReader) =
    {
        Dto.UserDetails.username = reader.GetString(0)
        Dto.UserDetails.firstName = reader.GetString(1)
        Dto.UserDetails.lastName = reader.GetString(2)
        Dto.UserDetails.language = if reader.IsDBNull(3) then "en" else reader.GetString(3)
        Dto.UserDetails.email = if reader.IsDBNull(4) then None else Some (reader.GetString(4))
    }

let baseUsersQuery = "SELECT login, firstname, lastname, language, address FROM users LEFT JOIN email_addresses ON users.id = email_addresses.user_id"

let manualUsersQuery (connString : string) (limit : int option) (offset : int option) =
    async {
        let sql = baseUsersQuery
        let! result = fetchData connString sql convertUserRow |> Async.AwaitTask
        return result |> List.ofArray
    }

let getUser (connString : string) username =
    async {
        let sql = baseUsersQuery + " WHERE login = @username"
        let setParams (cmd : MySqlCommand) =
            cmd.Parameters.AddWithValue("username", username) |> ignore
        let! result = fetchDataWithParams connString sql setParams convertUserRow |> Async.AwaitTask
        return result |> Array.tryHead
    }

let searchUsersLoose (connString : string) (searchTerm : string) =
    async {
        let sql = baseUsersQuery + " WHERE login LIKE @searchTerm OR firstname LIKE @searchTerm OR lastname LIKE @searchTerm OR address LIKE @searchTerm"
        let escapedSearchTerm = "%" + searchTerm.Replace(@"\", @"\\").Replace("%", @"\%") + "%"
        // TODO: Test whether that works, or whether we need to write something like "LIKE %@searchTerm%" instead
        let setParams (cmd : MySqlCommand) =
            cmd.Parameters.AddWithValue("searchTerm", escapedSearchTerm) |> ignore
        let! result = fetchDataWithParams connString sql setParams convertUserRow |> Async.AwaitTask
        return result |> List.ofArray
    }

let searchUsersExact (connString : string) (searchTerm : string) =
    async {
        let sql = baseUsersQuery + " WHERE login = @searchTerm OR firstname = @searchTerm OR lastname = @searchTerm OR address = @searchTerm"
        let! result = fetchDataWithParams connString sql (fun cmd -> cmd.Parameters.AddWithValue("searchTerm", searchTerm) |> ignore) convertUserRow |> Async.AwaitTask
        return result |> List.ofArray
    }

let toDict (s : string) =
    Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string,string>>(s)

let convertProjectRow (reader : MySqlDataReader) = {
        Dto.ProjectDetailsInternal.code = reader.GetString("identifier")
        Dto.ProjectDetailsInternal.name = reader.GetString("name")
        Dto.ProjectDetailsInternal.description = reader.GetString("description")
        Dto.ProjectDetailsInternal.membership = reader.GetString("user_roles") |> toDict  // TODO: Handle case where we don't want the complete membership list
    }

let baseProjectQuery = "SELECT identifier, name, description, \"{}\" FROM projects"

// TODO: Either we set a global MySQL setting, or we run the following before our projects query. Choosing the global setting for now since it's simpler.
// let projectsPreQuery = "SET SESSION sql_mode=(SELECT REPLACE(@@sql_mode,'ONLY_FULL_GROUP_BY',''))"

let projectWithMembersBaseQuery =
    "SELECT projects.identifier, projects.name, projects.description, json_objectagg(users.login, roles.name) AS user_roles" +
    " FROM projects" +
    " LEFT JOIN members ON members.project_id = projects.id" +
    " JOIN member_roles ON member_roles.member_id = members.id" +
    " JOIN roles ON roles.id = member_roles.role_id" +
    " JOIN users on members.user_id = users.id"
// MySQL wants WHERE clause before GROUP BY clause, so we add GROUP BY as a separate step
let projectsWithMembersGroupByClause = " GROUP BY projects.identifier"

let projectsQueryAsync (connString : string) =
    async {
        let sql = baseProjectQuery
        let setParams (cmd : MySqlCommand) =
            ()
        let! result = fetchDataWithParams connString sql setParams convertProjectRow |> Async.AwaitTask
        return result |> List.ofArray
    }

let projectsAndRolesQueryAsync (connString : string) =
    async {
        let sql = projectWithMembersBaseQuery + projectsWithMembersGroupByClause
        let! result = fetchData connString sql convertProjectRow |> Async.AwaitTask
        return result |> List.ofArray
    }

let projectsCountAsync (connString : string) =
    async {
        let sql = "SELECT COUNT(*) FROM projects"
        return! doCountQuery connString sql |> Async.AwaitTask
    }

let realProjectsCountAsync (connString : string) =
    async {
        let sql = baseProjectQuery
        let! result = fetchData connString sql convertProjectRow |> Async.AwaitTask
        return
            result
            |> Seq.map (fun project -> project, GuessProjectType.guessType project.code project.name project.description)
            |> Seq.filter (fun (project, projectType) -> projectType <> Test && not (project.code.StartsWith "test"))
            |> Seq.length
    }

let usersCountAsync (connString : string) =
    async {
        let sql = "SELECT COUNT(*) FROM users"
        return! doCountQuery connString sql |> Async.AwaitTask
    }

// TODO: Implement userExists, then use it later on below in upsertUser

let userExists (connString : string) username =
    async {
        let sql = "SELECT COUNT(*) FROM users WHERE login = @username"
        let setParams (cmd : MySqlCommand) =
            cmd.Parameters.AddWithValue("username", username) |> ignore
        let! count = doCountQueryWithParams connString sql setParams |> Async.AwaitTask
        return (count > 0)
    }

let projectExists (connString : string) projectCode =
    async {
        let sql = "SELECT COUNT(*) FROM projects WHERE identifier = @projectCode"
        let setParams (cmd : MySqlCommand) =
            cmd.Parameters.AddWithValue("projectCode", projectCode) |> ignore
        let! count = doCountQueryWithParams connString sql setParams |> Async.AwaitTask
        return (count > 0)
    }

let isAdmin (connString : string) username =
    async {
        let sql = "SELECT is_admin FROM users WHERE login = @username"
        let setParams (cmd : MySqlCommand) =
            cmd.Parameters.AddWithValue("username", username) |> ignore
        let convertRow (reader : MySqlDataReader) = reader.GetBoolean(0)
        let! results = fetchDataWithParams connString sql setParams convertRow |> Async.AwaitTask
        if results.Length > 0 then return results.[0] else return false
    }

let getProject (connString : string) projectCode =
    async {
        let sql = baseProjectQuery + " WHERE projects.identifier = @projectCode"
        let setParams (cmd : MySqlCommand) =
            cmd.Parameters.AddWithValue("projectCode", projectCode) |> ignore
        let! result = fetchDataWithParams connString sql setParams convertProjectRow |> Async.AwaitTask
        return result |> List.ofArray
    }

let getProjectWithRoles (connString : string) projectCode =
    async {
        let whereClause = " WHERE projects.identifier = @projectCode"
        let sql = projectWithMembersBaseQuery + whereClause + projectsWithMembersGroupByClause
        let setParams (cmd : MySqlCommand) =
            cmd.Parameters.AddWithValue("projectCode", projectCode) |> ignore
        let! result = fetchDataWithParams connString sql setParams convertProjectRow |> Async.AwaitTask
        return result |> List.ofArray
    }

let createProject (connString : string) (project : Api.CreateProject) =
    async {
        let sqlTxt = "INSERT INTO projects (name, description, identifier, status, created_on, updated_on) VALUES (@name, @description, @identifier, @status, NOW(), NOW())"
        use conn = new MySqlConnection(connString)
        do! conn.OpenAsync() |> Async.AwaitTask
        use cmd = new MySqlCommand(sqlTxt, conn)
        cmd.Parameters.AddWithValue("name", project.name) |> ignore
        cmd.Parameters.AddWithValue("description", project.description) |> ignore
        cmd.Parameters.AddWithValue("identifier", project.code) |> ignore
        cmd.Parameters.AddWithValue("status", ProjectStatus.Active) |> ignore
        let! result = cmd.ExecuteNonQueryAsync() |> Async.AwaitTask
        if result = 0 then return -1
        elif result < 0 then return result
        else
            let newId = int cmd.LastInsertedId
            return newId
    }

let createUserImpl (connString : string) (user : Api.CreateUser) (sql : string) =
    // TODO: Just make everything here a task{} instead of async{}; it'll be simpler
    async {
        // TODO: Password creation belongs in the controller, not the model. This requires a new CreateUserInternal data type which will carry the hashed password and the salt
        let salt = PasswordHashing.createSalt (Guid.NewGuid())
        let hashedPassword = PasswordHashing.hashPassword salt user.password
        use conn = new MySqlConnection(connString)
        do! conn.OpenAsync() |> Async.AwaitTask
        use transaction = conn.BeginTransaction()
        use cmd = new MySqlCommand(sql, conn, transaction)
        cmd.Parameters.AddWithValue("login", user.username) |> ignore
        cmd.Parameters.AddWithValue("firstname", user.firstName) |> ignore
        cmd.Parameters.AddWithValue("lastname", user.lastName) |> ignore
        cmd.Parameters.AddWithValue("hashedPassword", hashedPassword) |> ignore
        cmd.Parameters.AddWithValue("salt", salt) |> ignore
        cmd.Parameters.AddWithValue("status", UserStatus.Active) |> ignore
        let! result = cmd.ExecuteNonQueryAsync() |> Async.AwaitTask
        if result < 1 then
            do! transaction.RollbackAsync() |> Async.AwaitTask
            return (if result = 0 then -1 else result)
        else
            let didUpsert = result > 1
            // Inserted user, so insert email addresses as well
            let newUserId = int cmd.LastInsertedId
            match user.emailAddresses with
            | None -> return newUserId
            | Some email ->
                let sql = "INSERT INTO email_addresses (user_id, address, is_default, created_on, updated_on) " +
                          "VALUES (@userId, @email, 1, NOW(), NOW())"
                if didUpsert then ()  // TODO: Nope. INSERT ON DUPLICATE KEY UPDATE isn't going to work here because Redmine doesn't have a UNIQUE constraint on email addresses...!
                use cmd = new MySqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("user_id", newUserId) |> ignore
                cmd.Parameters.AddWithValue("address", email) |> ignore
                let! result = cmd.ExecuteNonQueryAsync() |> Async.AwaitTask
                if result < 1 then
                    do! transaction.RollbackAsync() |> Async.AwaitTask
                    return (if result = 0 then -1 else result)
                else
                    do! transaction.CommitAsync() |> Async.AwaitTask
                    return newUserId
    }

let createUser (connString : string) (user : Api.CreateUser) =
    let sql = "INSERT INTO users (login, firstname, lastname, hashed_password, salt, status, created_on, updated_on) " +
              "VALUES (@login, @firstname, @lastname, @hashedPassword, @salt, @status, NOW(), NOW())"
    createUserImpl connString user sql

let updateUser (connString : string) (login : string) (updatedUser : Api.CreateUser) =
    async {
        // Everyone may change their own data, but only admins may change some else's data
        let sql = "SELECT is_admin, login FROM users WHERE login = @login"
        let setParams (cmd : MySqlCommand) =
            cmd.Parameters.AddWithValue("login", updatedUser.login.username) |> ignore
            // TODO: Once we move "is this allowed?" logic into controller, verify password as well
        let! result = fetchDataWithParams connString sql setParams (fun row -> row.GetBoolean("is_admin"), row.GetString("login")) |> Async.AwaitTask
        if result.Length = 0 then
            // TODO: Edit to return a Result<unit, errorDU> so we can indicate why this may fail (e.g., "Invalid login" or whatever). In this case, login user not found
            // That errorDU should live in ErrorCodes.fs
            return ()
        let isAdmin, loggedInUser = result.[0]
        // TODO: Move "is this allowed?" logic into the controller, not here
        // TODO: Once we move "is this allowed?" logic into controller, verify password as well
        let allowed = isAdmin || loggedInUser = updatedUser.username
        if not allowed then
            // Return error indicating 403 forbidden
            return 0
        else
            let! salt =
                let sql = "SELECT salt FROM users where login = @username"
                let setParams (cmd : MySqlCommand) =
                    cmd.Parameters.AddWithValue("username", updatedUser.username) |> ignore
                doScalarQueryWithParams connString sql setParams |> Async.AwaitTask
            let isPasswordChange = not (String.IsNullOrEmpty updatedUser.password)
            let hashedPassword = if isPasswordChange then PasswordHashing.hashPassword salt updatedUser.password else ""
            let sql =
                if isPasswordChange then
                    "UPDATE users SET login = @username, hashed_password = @hashedPassword, must_change_passwd = @mustChangePassword, firstname = @firstName, lastname = @lastName, language = @language" +
                    " WHERE login = @loggedInUser"
                else
                    "UPDATE users SET login = @username, must_change_passwd = @mustChangePassword, firstname = @firstName, lastname = @lastName, language = @language" +
                    " WHERE login = @loggedInUser"
            let setParams (cmd : MySqlCommand) =
                cmd.Parameters.AddWithValue("login", updatedUser.username) |> ignore
                if isPasswordChange then
                    cmd.Parameters.AddWithValue("hashedPassword", hashedPassword) |> ignore
                cmd.Parameters.AddWithValue("mustChangePassword", updatedUser.mustChangePassword) |> ignore
                cmd.Parameters.AddWithValue("firstName", updatedUser.firstName) |> ignore
                cmd.Parameters.AddWithValue("lastName", updatedUser.lastName) |> ignore
                cmd.Parameters.AddWithValue("language", updatedUser.language |> Option.defaultValue "en") |> ignore
                cmd.Parameters.AddWithValue("loggedInUser", loggedInUser) |> ignore
            let! changedRows = doNonQueryWithParams connString sql setParams |> Async.AwaitTask
            // TODO: Detect changeRows being 0 and return an error code
            return changedRows
    }

let upsertUser (connString : string) (login : string) (updatedUser : Api.CreateUser) =
    async {
        let! shouldUpdate = userExists connString updatedUser.username
        if not shouldUpdate then
            return! createUser connString updatedUser
        else
            return! updateUser connString login updatedUser
    }
    // This won't work, because the Redmine data model doesn't have "login" as a unique key constraint (?!?)
    // let xql = "INSERT INTO users (login, firstname, lastname, hashed_password, salt, status, created_on, updated_on) " +
    //           "VALUES (@login, @firstname, @lastname, @hashedPassword, @salt, @status, NOW(), NOW()) " +
    //           "ON DUPLICATE KEY UPDATE firstname = @firstname, lastname = @lastname, hashedPassword = @hashedPassword, salt = @salt, status = @status, updated_on = NOW()"
    // createUserImpl connString updatedUser xql

// TODO: Do manual SQL statements from here on down (we've done up to here so far)

let projectsAndRolesQueryAsyncReminder (connString : string) =
    async {
        let sql = projectWithMembersBaseQuery + projectsWithMembersGroupByClause
        let! result = fetchData connString sql convertProjectRow |> Async.AwaitTask
        return result |> List.ofArray
    }

let projectsAndRolesBaseQuery =
    "SELECT projects.identifier AS identifier, roles.name AS role" +
    " FROM members" +
    " JOIN projects ON members.project_id = projects.id" +
    " JOIN member_roles ON member_roles.member_id = members.id" +
    " JOIN roles ON roles.id = member_roles.role_id" +
    " JOIN users on members.user_id = users.id"
let projectsAndRolesGroupByClause = " GROUP BY users.login"

let projectsAndRolesByUserImpl (connString : string) username = async {
    let whereClause = " WHERE users.login = @username"
    let xql = projectsAndRolesBaseQuery + whereClause + projectsAndRolesGroupByClause
    let setParams (cmd : MySqlCommand) =
            cmd.Parameters.AddWithValue("username", username) |> ignore
    let convertRow (reader : MySqlDataReader) =
        reader.GetString("identifier"), reader.GetString("role")
    let! result = fetchDataWithParams connString xql setParams convertRow |> Async.AwaitTask
    return result |> List.ofArray
}
// TODO: Figure out how to "pass through" the result from json_objectagg

let getProjectDetails (conn : MySqlConnection) (projectCode : string) = async {
    let sql = baseProjectQuery + "WHERE identifier = @projectCode"
    use cmd = new MySqlCommand(sql, conn)
    cmd.Parameters.AddWithValue("projectCode", projectCode) |> ignore
    use! reader = cmd.ExecuteReaderAsync() |> Async.AwaitTask
    let! result = reader :?> MySqlDataReader |> getSqlResult convertProjectRow |> Async.AwaitTask
    return result
}

let projectsAndRolesByUser (connString : string) username = async {
    let! projectsAndRoles = projectsAndRolesByUserImpl connString username
    return projectsAndRoles
}

let projectsAndRolesByUserRole connString username (roleName : string) = async {
    let! projectsAndRoles = projectsAndRolesByUserImpl connString username
    return projectsAndRoles |> List.filter (fun (proj, role) -> role = roleName)
}

let projectsByUserRole connString username (roleName : string) = async {
    let! projectsAndRoles = projectsAndRolesByUserImpl connString username
    return projectsAndRoles |> List.filter (fun (proj, role) -> role = roleName) |> List.map fst
}

let projectsByUser connString username = async {
    let! projectsAndRoles = projectsAndRolesByUserImpl connString username
    return projectsAndRoles |> List.map fst
}

let roleNames (connString : string) =
    async {
        let sql = "SELECT id, name FROM roles"
        let convertRow (reader : MySqlDataReader) =
            reader.GetInt32("id"), reader.GetString("name")
        return! fetchData connString sql convertRow |> Async.AwaitTask
    }

let verifyPass (clearPass : string) (salt : string) (hashPass : string) =
    let calculatedHash = PasswordHashing.hashPassword salt clearPass
    calculatedHash = hashPass

let verifyLoginInfo (connString : string) (loginCredentials : Api.LoginCredentials) =
    // During development of the client UI, just accept any credentials. TODO: Natually, restore real code before going to production
    async { return true }
    // async {
    //     let sql = "SELECT salt, hashed_password FROM users where login = @username"
    //     let setParams (cmd : MySqlCommand) =
    //         cmd.Parameters.AddWithValue("username", loginCredentials.username) |> ignore
    //     let convertRow (reader : MySqlDataReader) =
    //         reader.GetString("salt"), reader.GetString("hashed_password")
    //     let rows = fetchDataWithParams connString sql setParams convertRow |> Async.AwaitTask
    //     if rows.Length > 0 then
    //         let salt, hashedPassword = rows.[0]
    //         return verifyPass loginCredentials.password salt hashedPassword
    //     else
    //         return false
    // }

let changePassword (connString : string) (login : string) (changeRequest : Api.ChangePassword) =
    async {
        let! salt =
            let sql = "SELECT salt FROM users where login = @username"
            let setParams (cmd : MySqlCommand) =
                cmd.Parameters.AddWithValue("username", changeRequest.username) |> ignore
            doScalarQueryWithParams connString sql setParams |> Async.AwaitTask
        let hashedPassword = PasswordHashing.hashPassword salt changeRequest.password
        let sql = "UPDATE users SET hashed_password = @hashedPassword, must_change_passwd = @mustChangePassword, updated_on = NOW() WHERE login = @username"
        let setParams (cmd : MySqlCommand) =
            cmd.Parameters.AddWithValue("hashedPassword", hashedPassword) |> ignore
            cmd.Parameters.AddWithValue("mustChangePassword", changeRequest.mustChangePassword |> Option.defaultValue false) |> ignore
        let! result = doNonQueryWithParams connString sql setParams |> Async.AwaitTask
        return (result = 1)
    }

let removeMembership (connString : string) (username : string) (projectCode : string) =
    async {
        use conn = new MySqlConnection(connString)
        do! conn.OpenAsync() |> Async.AwaitTask
        use transaction = conn.BeginTransaction()
        let sql =
            "SELECT id FROM members" +
            " JOIN users ON users.id = members.user_id" +
            " JOIN projects ON projects.id = members.project_id" +
            " WHERE users.login = @username AND projects.identifier = @projectCode"
        use cmd = new MySqlCommand(sql, conn, transaction)
        cmd.Parameters.AddWithValue("username", username) |> ignore
        cmd.Parameters.AddWithValue("projectCode", projectCode) |> ignore
        use! reader = cmd.ExecuteReaderAsync() |> Async.AwaitTask
        let! memberRowIds = reader :?> MySqlDataReader |> getSqlResult (fun reader -> reader.GetInt32(0)) |> Async.AwaitTask
        // Also have to delete from member_roles table
        let sql = "DELETE FROM member_roles WHERE member_id IN @memberRowIds"
        use cmd = new MySqlCommand(sql, conn, transaction)
        cmd.Parameters.AddWithValue("memberRowIds", memberRowIds) |> ignore
        let! _ = cmd.ExecuteNonQueryAsync() |> Async.AwaitTask
        let sql = "DELETE FROM members WHERE id IN @memberRowIds"
        use cmd = new MySqlCommand(sql, conn, transaction)
        cmd.Parameters.AddWithValue("memberRowIds", memberRowIds) |> ignore
        let! _ = cmd.ExecuteNonQueryAsync() |> Async.AwaitTask
        do! transaction.CommitAsync() |> Async.AwaitTask
        return ()
    }

let addMembership (connString : string) (username : string) (projectCode : string) (roleName : string) =

    let getId (sql : string) (paramName : string) (paramValue : string) (conn : MySqlConnection) (transaction: MySqlTransaction) = task {
            use cmd = new MySqlCommand(sql, conn, transaction)
            cmd.Parameters.AddWithValue(paramName, paramValue) |> ignore
            use! reader = cmd.ExecuteReaderAsync()
            let! ids = reader :?> MySqlDataReader |> getSqlResult (fun reader -> reader.GetInt32(0))
            if ids.Length = 0 then
                return -1
            else
                return ids.[0]
    }

    async {
        use conn = new MySqlConnection(connString)
        do! conn.OpenAsync() |> Async.AwaitTask
        use transaction = conn.BeginTransaction()

        let! roleId = getId "SELECT id FROM roles WHERE name = @roleName" "roleName" roleName conn transaction |> Async.AwaitTask
        let! userId = getId "SELECT id FROM users WHERE login = @username" "username" username conn transaction |> Async.AwaitTask
        let! projId = getId "SELECT id FROM projects WHERE identifier = @projectCode" "projectCode" projectCode conn transaction |> Async.AwaitTask
        if roleId < 0 || userId < 0 || projId < 0 then
            // Can't do anything with invalid data
            do! transaction.RollbackAsync() |> Async.AwaitTask
            return ()
        else
            // First, check whether user is already a member
            let sql =
                "SELECT id FROM members" +
                " JOIN users ON users.id = members.user_id" +
                " JOIN projects ON projects.id = members.project_id" +
                " WHERE users.login = @username AND projects.identifier = @projectCode"
            use cmd = new MySqlCommand(sql, conn, transaction)
            cmd.Parameters.AddWithValue("username", username) |> ignore
            cmd.Parameters.AddWithValue("projectCode", projectCode) |> ignore
            use! reader = cmd.ExecuteReaderAsync() |> Async.AwaitTask
            let! memberRowIds = reader :?> MySqlDataReader |> getSqlResult (fun reader -> reader.GetInt32(0)) |> Async.AwaitTask
            if memberRowIds.Length > 0 then
                // Already a member; just update the membership role that user already has
                let sql = "UPDATE member_roles SET role_id = @roleId WHERE member_id = @memberId"
                use cmd = new MySqlCommand(sql, conn, transaction)
                cmd.Parameters.AddWithValue("username", username) |> ignore
                cmd.Parameters.AddWithValue("projectCode", projectCode) |> ignore
                let! _ = cmd.ExecuteNonQueryAsync() |> Async.AwaitTask
                do! transaction.CommitAsync() |> Async.AwaitTask
                return ()
            else
                // Add new members *and* member_roles entries
                let sql =
                    "INSERT INTO members (user_id, project_id, created_on) " +
                    "VALUES (@userId, @projectId, NOW())"
                use cmd = new MySqlCommand(sql, conn, transaction)
                cmd.Parameters.AddWithValue("userId", userId) |> ignore
                cmd.Parameters.AddWithValue("projectId", projId) |> ignore
                let! affectedRows = cmd.ExecuteNonQueryAsync() |> Async.AwaitTask
                if affectedRows <= 0 then
                    do! transaction.RollbackAsync() |> Async.AwaitTask
                    return ()
                let memberId = cmd.LastInsertedId
                let sql =
                    "INSERT INTO member_roles (member_id, role_id) " +
                    "VALUES (@memberId, @roleId)"
                use cmd = new MySqlCommand(sql, conn, transaction)
                cmd.Parameters.AddWithValue("memberId", memberId) |> ignore
                cmd.Parameters.AddWithValue("roleId", roleId) |> ignore
                let! affectedRows = cmd.ExecuteNonQueryAsync() |> Async.AwaitTask
                if affectedRows <= 0 then
                    do! transaction.RollbackAsync() |> Async.AwaitTask
                    return ()
                else
                    do! transaction.CommitAsync() |> Async.AwaitTask
                    return ()
    }

let archiveProject (connString : string) (projectCode : string) =
    async {
        use conn = new MySqlConnection(connString)
        do! conn.OpenAsync() |> Async.AwaitTask
        use transaction = conn.BeginTransaction()

        let sql = sprintf "UPDATE projects SET status = @status WHERE identifier = @projectCode"
        let setParams (cmd : MySqlCommand) =
            cmd.Parameters.AddWithValue("status", ProjectStatus.Archived) |> ignore
            cmd.Parameters.AddWithValue("projectCode", projectCode) |> ignore
        let! affectedRows = doNonQueryWithParams connString sql setParams |> Async.AwaitTask
        if affectedRows > 0 then
            do! transaction.CommitAsync() |> Async.AwaitTask
            return true
        else
            do! transaction.RollbackAsync() |> Async.AwaitTask
            return false
    }

// NOTE: If you change the database schema, you must UNCOMMENT the next two lines and then
// reload Visual Studio or VS Code. (Yes, just reload your *editor*). This will cause the
// updated schema to be saved to the location defined in schemaPath.)

// let ctx = sql.GetDataContext sampleConnString
// ctx.SaveContextSchema()

module ModelRegistration =
    open Microsoft.Extensions.DependencyInjection
    open Microsoft.Extensions.DependencyInjection.Extensions

    let registerServices (builder : IServiceCollection) (connString : string) =
        // We need to turn off MySQL's ONLY_FULL_GROUP_BY setting for our entire session
        builder
            .RemoveAll<ListUsers>()
            .AddSingleton<ListUsers>(manualUsersQuery)
            .RemoveAll<ListProjects>()
            .AddSingleton<ListProjects>(projectsAndRolesQueryAsync)
            .RemoveAll<CountUsers>()
            .AddSingleton<CountUsers>(CountUsers (usersCountAsync))
            .RemoveAll<CountProjects>()
            .AddSingleton<CountProjects>(CountProjects (projectsCountAsync))
            .RemoveAll<CountRealProjects>()
            .AddSingleton<CountRealProjects>(CountRealProjects (realProjectsCountAsync))
            .RemoveAll<ListRoles>()
            .AddSingleton<ListRoles>(roleNames)
            .RemoveAll<UserExists>()
            .AddSingleton<UserExists>(UserExists (userExists))
            .RemoveAll<ProjectExists>()
            .AddSingleton<ProjectExists>(ProjectExists (projectExists))
            .RemoveAll<IsAdmin>()
            .AddSingleton<IsAdmin>(IsAdmin (isAdmin))
            .RemoveAll<SearchUsersExact>()
            .AddSingleton<SearchUsersExact>(SearchUsersExact (searchUsersExact))
            .RemoveAll<SearchUsersLoose>()
            .AddSingleton<SearchUsersLoose>(SearchUsersLoose (searchUsersLoose))
            .RemoveAll<GetUser>()
            .AddSingleton<GetUser>(getUser)
            .RemoveAll<GetProject>()
            .AddSingleton<GetProject>(getProjectWithRoles)
            .RemoveAll<CreateProject>()
            .AddSingleton<CreateProject>(createProject)
            .RemoveAll<CreateUser>()
            .AddSingleton<CreateUser>(createUser)
            .RemoveAll<UpsertUser>()
            .AddSingleton<UpsertUser>(upsertUser)
            .RemoveAll<ChangePassword>()
            .AddSingleton<ChangePassword>(changePassword)
            .RemoveAll<ProjectsByUser>()
            .AddSingleton<ProjectsByUser>(projectsByUser)
            .RemoveAll<ProjectsByUserRole>()
            .AddSingleton<ProjectsByUserRole>(projectsByUserRole)
            .RemoveAll<ProjectsAndRolesByUserRole>()
            .AddSingleton<ProjectsAndRolesByUserRole>(projectsAndRolesByUserRole)
            .RemoveAll<ProjectsAndRolesByUser>()
            .AddSingleton<ProjectsAndRolesByUser>(projectsAndRolesByUser)
            .RemoveAll<VerifyLoginCredentials>()
            .AddSingleton<VerifyLoginCredentials>(verifyLoginInfo)
            .RemoveAll<AddMembership>()
            .AddSingleton<AddMembership>(AddMembership addMembership)
            .RemoveAll<RemoveMembership>()
            .AddSingleton<RemoveMembership>(RemoveMembership removeMembership)
            .RemoveAll<RemoveUserFromAllRolesInProject>()
            .AddSingleton<RemoveUserFromAllRolesInProject>(RemoveUserFromAllRolesInProject (removeUserFromAllRolesInProject))
            .RemoveAll<ArchiveProject>()
            .AddSingleton<ArchiveProject>(archiveProject)
        |> ignore
        FSharp.Data.Sql.Common.QueryEvents.SqlQueryEvent |> Event.add (printfn "Executing SQL: %O")
