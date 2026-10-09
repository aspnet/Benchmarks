use std::{fmt, sync::Arc};

use axum::{
    Json, Router,
    extract::{Path, State, rejection::PathRejection},
    http::StatusCode,
    routing::get,
};
use serde::Serialize;

#[derive(Clone, Copy, Serialize)]
#[serde(rename_all = "camelCase")]
struct Todo {
    id: u8,
    title: &'static str,
    due_by: Option<&'static str>,
    is_complete: bool,
}

type Todos = Arc<[Todo; 5]>;

pub fn router() -> Router {
    let todos = Arc::new([
        Todo {
            id: 1,
            title: "Walk the dog",
            due_by: None,
            is_complete: false,
        },
        Todo {
            id: 2,
            title: "Do the dishes",
            due_by: Some("2026-01-01"),
            is_complete: false,
        },
        Todo {
            id: 3,
            title: "Do the laundry",
            due_by: Some("2026-01-02"),
            is_complete: false,
        },
        Todo {
            id: 4,
            title: "Clean the bathroom",
            due_by: None,
            is_complete: false,
        },
        Todo {
            id: 5,
            title: "Clean the car",
            due_by: Some("2026-01-03"),
            is_complete: false,
        },
    ]);

    Router::new()
        .route("/todos", get(list_todos))
        .route("/todos/{id}", get(get_todo))
        .route("/healthz", get(|| async { "ready" }))
        .fallback(|| async { StatusCode::NOT_FOUND })
        .with_state(todos)
}

async fn list_todos(State(todos): State<Todos>) -> Json<Todos> {
    Json(todos)
}

async fn get_todo(
    State(todos): State<Todos>,
    path: Result<Path<String>, PathRejection>,
) -> Result<Json<Todo>, StatusCode> {
    let Path(id) = path.map_err(|_| StatusCode::NOT_FOUND)?;
    if id.starts_with('0') || !id.bytes().all(|byte| byte.is_ascii_digit()) {
        return Err(StatusCode::NOT_FOUND);
    }
    let index = id
        .parse::<usize>()
        .ok()
        .and_then(|id| id.checked_sub(1))
        .ok_or(StatusCode::NOT_FOUND)?;
    todos
        .get(index)
        .copied()
        .map(Json)
        .ok_or(StatusCode::NOT_FOUND)
}

#[derive(Debug, PartialEq, Eq)]
pub struct InvalidPort;

impl fmt::Display for InvalidPort {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("PORT must contain only decimal digits and be in the range 1..=65535")
    }
}

impl std::error::Error for InvalidPort {}

pub fn parse_port(value: Option<&str>) -> Result<u16, InvalidPort> {
    let Some(value) = value else {
        return Ok(8080);
    };
    if value.is_empty() || !value.bytes().all(|byte| byte.is_ascii_digit()) {
        return Err(InvalidPort);
    }
    value
        .parse::<u16>()
        .ok()
        .filter(|port| *port != 0)
        .ok_or(InvalidPort)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn port_defaults_and_boundaries() {
        assert_eq!(parse_port(None), Ok(8080));
        for (value, expected) in [("1", 1), ("8080", 8080), ("65535", 65535), ("00080", 80)] {
            assert_eq!(parse_port(Some(value)), Ok(expected));
        }
    }

    #[test]
    fn invalid_ports_are_rejected() {
        for value in [
            "",
            "0",
            "000",
            "65536",
            "-1",
            "+80",
            " 80",
            "80 ",
            "8.0",
            "abc",
            "\u{ff18}\u{ff10}",
            "999999999999999999999999999999",
        ] {
            assert_eq!(parse_port(Some(value)), Err(InvalidPort), "{value:?}");
        }
    }
}
