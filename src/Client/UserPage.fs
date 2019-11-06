module UserPage

open Browser
open Elmish
open Fable.Core.JsInterop
open Fable.React
open Fable.React.Props
open Fulma
// open Thoth.Elmish.Toast
open Thoth.Fetch
open Thoth.Json

open TextInput
open Shared
open JsonHelpers

type Msg =
    | FindUser of string
    | UserFound of User
    | UserNotFound
    | LogUserResult of User
    | RoleListFetchFailed of exn
    | RoleListUpdated of JsonResult<Role list>
    | NewUserPageNav of string
    | AddProject of string
    | DelProject of string
    | LogResult of Result<string,string>
    | GetProjectsForUser
    | GetProjectsByRole of int
    | ProjectsListRetrieved of (Project * Role) list
    | LogException of System.Exception

type Model = { RoleList : Role list; ProjectList : (Project * Role) list; CurrentlyViewedUser : SharedUser option; }

let init() =
    { RoleList = []; ProjectList = []; CurrentlyViewedUser = None },
    Cmd.OfPromise.either Fetch.get "/api/roles" RoleListUpdated RoleListFetchFailed

let update (msg : Msg) (currentModel : Model) : Model * Cmd<Msg> =
    match msg with
    | RoleListFetchFailed e ->
        if e.Message.StartsWith("504") then
            // 504 Gateway Timeout usually simply means that we're recompiling
            let tryAgain () = promise {
                do! Promise.sleep 3000
                // Compiler needs type hint here since it's not on same line as Cmd.OfPromise.either
                return! Fetch.get<JsonResult<Role list>> "/api/roles" }
            currentModel, Cmd.OfPromise.either tryAgain () RoleListUpdated RoleListFetchFailed
        else
            currentModel, Notifications.notifyException e
    | FindUser username ->
        let url = sprintf "/api/users/%s" username
        currentModel, Cmd.OfPromise.either Fetch.get url LogUserResult LogException
    | UserFound user ->
        let nextModel = { currentModel with CurrentlyViewedUser = Some { Name = sprintf "%s %s" user.FirstName user.LastName; Email = "unknown@example.com" (* Can't get email until we redesign the API *) } }
        nextModel, Cmd.ofMsg (LogUserResult user)
    | UserNotFound ->
        currentModel, Cmd.none
    | LogUserResult user ->
        printfn "User ID %d, first name %s, last name %s, is_admin %A" user.Id user.FirstName user.LastName user.Admin
        currentModel, Cmd.none
    | RoleListUpdated jsonResult ->
        match toResult jsonResult with
        | Ok newRoleList ->
            printfn "Got role list: %A" newRoleList
            let nextModel = { currentModel with RoleList = newRoleList }
            nextModel, Cmd.none
        | Error msg ->
            currentModel, Notifications.notifyError msg
    | NewUserPageNav username ->
        let nextModel = { currentModel with CurrentlyViewedUser = Some { Name = username; Email = "rmunn@pobox.com" } }
        nextModel, Cmd.none
    | GetProjectsForUser ->
        match currentModel.CurrentlyViewedUser with
        | None ->
            currentModel, Cmd.none
        | Some user ->
            let url = sprintf "/api/users/%s/projects" user.Name
            let data = { username = user.Name; password = "s3kr3t" }
            currentModel, Cmd.OfPromise.either (fun data -> Fetch.post(url, data)) data ProjectsListRetrieved LogException
    | GetProjectsByRole roleId ->
        match currentModel.CurrentlyViewedUser with
        | None ->
            currentModel, Cmd.none
        | Some user ->
            let url = sprintf "/api/users/%s/projects/withRole/%d" user.Name roleId
            let data = { username = user.Name; password = "s3kr3t" }
            currentModel, Cmd.OfPromise.either (fun data -> Fetch.post(url, data)) data ProjectsListRetrieved LogException
    | ProjectsListRetrieved projects ->
        let nextModel = { currentModel with ProjectList = projects }
        nextModel, Cmd.none
    | AddProject projCode ->
        match currentModel.CurrentlyViewedUser with
        | None ->
            currentModel, Cmd.none
        | Some user ->
            let data = { addUser = Some user; removeUser = None }
            let url = sprintf "/api/project/%s" projCode
            let promise = Fetch.patch(url, data) |> Promise.map LogResult
            currentModel, Cmd.OfPromise.result promise
    | DelProject projCode ->
        match currentModel.CurrentlyViewedUser with
        | None ->
            currentModel, Cmd.none
        | Some user ->
            let data = { addUser = None; removeUser = Some user }
            let url = sprintf "/api/project/%s" projCode
            let promise = Fetch.patch(url, data) |> Promise.map LogResult
            currentModel, Cmd.OfPromise.result promise
    | LogResult result ->
        let cmd = result |> Notifications.notifyStrResult
        currentModel, cmd
    | LogException exn ->
        let cmd = Notifications.notifyError exn.Message
        currentModel, cmd

let RoleSelector =
    FunctionComponent.Of (fun (props : {| model : Model; dispatch : Msg -> unit |}) ->
        let selected = Hooks.useState "0"
        Select.select
            [ Select.IsLoading (props.model.RoleList |> List.isEmpty) ]
            [ select [ OnChange (fun ev -> selected.update ev.Value) ] [ for role in props.model.RoleList -> option [ Value (role.Id.ToString()); Key (role.Id.ToString()) ] [ str role.Name ] ]
              Button.a
                [ Button.Size IsSmall
                  Button.Color IsPrimary
                  Button.OnClick (fun _ -> printfn "Selected %A" selected.current; selected.current |> System.Int32.Parse |> GetProjectsByRole |> props.dispatch ) ]
                [ str "By Role" ] ]
)

let roleSelector (model : Model) (dispatch : Msg -> unit) =
    RoleSelector {| model = model; dispatch = dispatch |}

let view (model : Model) (dispatch : Msg -> unit) =
    let name = match model.CurrentlyViewedUser with None -> "" | Some user -> user.Name
    div [ ] [ str ("This is the user page" + if System.String.IsNullOrEmpty name then "" else " for " + name)
              br [ ]
              textInputComponent "Project code" "" (str "+") (dispatch << AddProject)
              br [ ]
              textInputComponent "Project code" "" (str "-") (dispatch << DelProject)
              Button.a
                [ Button.Size IsSmall
                  Button.Color IsPrimary
                  Button.OnClick (fun _ -> dispatch GetProjectsForUser) ]
                [ str "Projects" ]
              ul [ ]
                 [ for (project, role) in model.ProjectList -> li [ ] [ str ((project.Identifier |> Option.defaultValue "(no id)") + ": " + role.Name) ] ]
              roleSelector model dispatch
              br [ ]
              br [ ]
              textInputComponent "Find user" "" (Button.button [ Button.Color IsPrimary ] [ str "Find user" ]) (dispatch << FindUser)
            ]
