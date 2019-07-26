namespace Shared

type SharedUser = {
    Name : string
    Projects : string list
}

type AddUserToProjects = {
    Add : SharedUser
}

type RemoveUserFromProjects = {
    Remove : SharedUser
}

type PatchProjects =
    | Add of AddUserToProjects
    | Remove of RemoveUserFromProjects
