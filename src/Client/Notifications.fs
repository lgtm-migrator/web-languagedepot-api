module Notifications

open Fable.FontAwesome
open Elmish.React
open Fable.React
open Fable.React.Props
open Fulma
open Thoth.Elmish

let notifyOk msg =
    Toast.message msg
    |> Toast.position Toast.TopCenter
    |> Toast.timeout (System.TimeSpan.FromSeconds 3.0)
    |> Toast.withCloseButton
    |> Toast.success

let notifyError msg =
    Toast.message msg
    |> Toast.position Toast.TopCenter
    |> Toast.timeout (System.TimeSpan.FromSeconds 15.0)
    |> Toast.withCloseButton
    |> Toast.error

let notifyStrResult (result : Result<string, string>) =
    match result with
    | Ok s -> notifyOk s
    | Error s -> notifyError s

let renderToastWithFulma =
        { new Toast.IRenderer<Fa.IconOption> with
            member __.Toast children color =
                Notification.notification [ Notification.CustomClass color ]
                    children

            member __.CloseButton onClick =
                Notification.delete [ Props [ OnClick onClick ] ]
                    [ ]

            member __.InputArea children =
                Columns.columns [ Columns.IsGapless
                                  Columns.Modifiers [ Modifier.TextAlignment (Screen.All, TextAlignment.Centered) ]
                                  Columns.CustomClass "notify-inputs-area" ]
                    children

            member __.Input (txt : string) (callback : (unit -> unit)) =
                Column.column [ ]
                    [ Button.button [ Button.OnClick (fun _ -> callback ())
                                      Button.Color IsWhite ]
                        [ str txt ] ]

            member __.Title txt =
                Heading.h5 []
                           [ str txt ]

            member __.Icon (icon : Fa.IconOption) =
                Icon.icon [ Icon.Size IsMedium ]
                    [ Fa.i [ icon
                             Fa.Size Fa.Fa2x ]
                        [ ] ]

            member __.SingleLayout title message =
                div [ ]
                    [ title; message ]

            member __.Message txt =
                span [ ]
                     [ str txt ]

            member __.SplittedLayout iconView title message =
                Columns.columns [ Columns.IsGapless
                                  Columns.IsVCentered ]
                    [ Column.column [ Column.Width (Screen.All, Column.Is2) ]
                        [ iconView ]
                      Column.column [ ]
                        [ title
                          message ] ]

            member __.StatusToColor status =
                match status with
                | Toast.Success -> "is-success"
                | Toast.Warning -> "is-warning"
                | Toast.Error -> "is-danger"
                | Toast.Info -> "is-info" }

let withNotifications = Toast.Program.withToast
