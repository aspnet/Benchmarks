use axum::{
    Router,
    body::{Body, to_bytes},
    http::{Method, Request, StatusCode, header},
};
use tower::ServiceExt;

const EXPECTED: &[u8] = include_bytes!("fixtures/minimal-todo-v1.expected.json");

async fn request(app: &Router, method: Method, path: &str) -> axum::response::Response {
    app.clone()
        .oneshot(
            Request::builder()
                .method(method)
                .uri(path)
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap()
}

async fn assert_empty(app: &Router, method: Method, path: &str, status: StatusCode) {
    let response = request(app, method, path).await;
    assert_eq!(response.status(), status, "{path}");
    assert!(!response.headers().contains_key(header::LOCATION));
    assert!(
        to_bytes(response.into_body(), 4096)
            .await
            .unwrap()
            .is_empty()
    );
}

fn expected_items() -> Vec<String> {
    let text = std::str::from_utf8(EXPECTED).unwrap();
    assert!(text.starts_with('[') && text.ends_with(']'));
    text[1..text.len() - 1]
        .split("},{")
        .enumerate()
        .map(|(index, part)| {
            let prefix = if index == 0 { "" } else { "{" };
            let suffix = if index == 4 { "" } else { "}" };
            format!("{prefix}{part}{suffix}")
        })
        .collect()
}

#[tokio::test]
async fn list_is_exact_compact_json_without_redirect() {
    let response = request(&minimal_todo::router(), Method::GET, "/todos").await;
    assert_eq!(response.status(), StatusCode::OK);
    assert_eq!(response.headers()[header::CONTENT_TYPE], "application/json");
    assert!(!response.headers().contains_key(header::LOCATION));
    assert_eq!(
        to_bytes(response.into_body(), 4096).await.unwrap().as_ref(),
        EXPECTED
    );

    let values: Vec<serde_json::Value> = serde_json::from_slice(EXPECTED).unwrap();
    assert_eq!(values.len(), 5);
    for (index, value) in values.iter().enumerate() {
        assert_eq!(value["id"], index + 1);
        assert!(value.get("dueBy").is_some());
        assert!(value["isComplete"].is_boolean());
    }
}

#[tokio::test]
async fn every_individual_item_has_exact_bytes() {
    let app = minimal_todo::router();
    for (index, expected) in expected_items().iter().enumerate() {
        let response = request(&app, Method::GET, &format!("/todos/{}", index + 1)).await;
        assert_eq!(response.status(), StatusCode::OK);
        assert_eq!(response.headers()[header::CONTENT_TYPE], "application/json");
        assert_eq!(
            to_bytes(response.into_body(), 4096).await.unwrap().as_ref(),
            expected.as_bytes()
        );
    }
}

#[tokio::test]
async fn missing_and_malformed_ids_are_empty_not_found() {
    let app = minimal_todo::router();
    for id in [
        "6",
        "999",
        "abc",
        "0",
        "01",
        "-1",
        "+1",
        "1.0",
        "1e0",
        "%20",
        "%201",
        "1%20",
        "%FF",
        "999999999999999999999999999999999999999",
    ] {
        assert_empty(
            &app,
            Method::GET,
            &format!("/todos/{id}"),
            StatusCode::NOT_FOUND,
        )
        .await;
    }
}

#[tokio::test]
async fn mutations_are_empty_method_not_allowed_and_do_not_change_data() {
    let app = minimal_todo::router();
    for method in [Method::POST, Method::PUT, Method::PATCH, Method::DELETE] {
        for path in [
            "/todos", "/todos/1", "/todos/2", "/todos/3", "/todos/4", "/todos/5",
        ] {
            let response = app
                .clone()
                .oneshot(
                    Request::builder()
                        .method(method.clone())
                        .uri(path)
                        .header(header::CONTENT_TYPE, "application/json")
                        .body(Body::from(r#"{"title":"Changed","isComplete":true}"#))
                        .unwrap(),
                )
                .await
                .unwrap();
            assert_eq!(response.status(), StatusCode::METHOD_NOT_ALLOWED);
            assert!(
                to_bytes(response.into_body(), 4096)
                    .await
                    .unwrap()
                    .is_empty()
            );
        }
    }
    let response = request(&app, Method::GET, "/todos").await;
    assert_eq!(
        to_bytes(response.into_body(), 4096).await.unwrap().as_ref(),
        EXPECTED
    );
}

#[tokio::test]
async fn unknown_paths_are_empty_not_found() {
    let app = minimal_todo::router();
    for path in ["/", "/unknown", "/todos/1/extra", "/favicon.ico"] {
        assert_empty(&app, Method::GET, path, StatusCode::NOT_FOUND).await;
    }
}

#[tokio::test]
async fn health_is_ready_plain_text() {
    let response = request(&minimal_todo::router(), Method::GET, "/healthz").await;
    assert_eq!(response.status(), StatusCode::OK);
    assert_eq!(
        response.headers()[header::CONTENT_TYPE],
        "text/plain; charset=utf-8"
    );
    assert_eq!(
        to_bytes(response.into_body(), 4096).await.unwrap().as_ref(),
        b"ready"
    );
}

#[tokio::test]
async fn representation_is_not_compressed_negotiated_or_conditional() {
    let app = minimal_todo::router();
    for path in ["/todos", "/todos/1", "/healthz"] {
        let response = app
            .clone()
            .oneshot(
                Request::builder()
                    .uri(path)
                    .header(header::ACCEPT_ENCODING, "gzip, br, deflate, zstd")
                    .header(header::ACCEPT, "application/xml")
                    .header(header::IF_NONE_MATCH, "*")
                    .header(header::IF_MODIFIED_SINCE, "Wed, 01 Jan 2099 00:00:00 GMT")
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::OK);
        for name in [
            header::CONTENT_ENCODING,
            header::ETAG,
            header::LAST_MODIFIED,
            header::CACHE_CONTROL,
            header::LOCATION,
        ] {
            assert!(!response.headers().contains_key(name));
        }
        let body = to_bytes(response.into_body(), 4096).await.unwrap();
        match path {
            "/todos" => assert_eq!(body.as_ref(), EXPECTED),
            "/todos/1" => assert_eq!(body.as_ref(), expected_items()[0].as_bytes()),
            _ => assert_eq!(body.as_ref(), b"ready"),
        }
    }
}

#[tokio::test]
async fn repeated_and_concurrent_reads_preserve_the_fixture() {
    let app = minimal_todo::router();
    let mut tasks = tokio::task::JoinSet::new();
    for _ in 0..32 {
        let app = app.clone();
        tasks.spawn(async move {
            for _ in 0..8 {
                let response = request(&app, Method::GET, "/todos").await;
                assert_eq!(response.status(), StatusCode::OK);
                assert_eq!(
                    to_bytes(response.into_body(), 4096).await.unwrap().as_ref(),
                    EXPECTED
                );
            }
        });
    }
    while let Some(result) = tasks.join_next().await {
        result.unwrap();
    }
    let response = request(&app, Method::GET, "/todos").await;
    assert_eq!(
        to_bytes(response.into_body(), 4096).await.unwrap().as_ref(),
        EXPECTED
    );
}
