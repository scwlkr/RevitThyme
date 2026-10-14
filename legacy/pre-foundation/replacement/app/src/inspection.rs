pub use crate::inspection_contract::*;
use crate::{
    contract::{ApiError, Target},
    native::Client,
    state::error,
};
use revitthyme_core::health::{self, Category};
use serde_json::{Value, json};

pub async fn context(client: &mut Client) -> Result<Context, ApiError> {
    let r = client.request("context", None, String::new(), None);
    let reply = client.complete_read(r).await?;
    let value = reply
        .inspection
        .and_then(|r| r.context)
        .ok_or_else(|| error(&reply.code, &reply.message, true))?;
    client.verify_target(&value.target)?;
    if reply.status != "inspected" || value.internal_length_unit != "ft" {
        return Err(error(
            "native_protocol",
            "Unexpected native context response.",
            true,
        ));
    }
    Ok(value)
}
pub async fn elements(client: &mut Client, target: Target, query: Query) -> Result<Page, ApiError> {
    query.validate()?;
    client.verify_target(&target)?;
    let mut r = client.request("elements", Some(target.clone()), String::new(), None);
    r.query = Some(query.clone());
    let reply = client.complete_read(r).await?;
    let page = reply
        .inspection
        .and_then(|r| r.page)
        .ok_or_else(|| error(&reply.code, &reply.message, true))?;
    let mut previous = query.after_id.parse::<i64>().unwrap();
    let valid_ids = page.items.iter().all(|item| {
        item.element_id.parse::<i64>().is_ok_and(|id| {
            let valid = id > previous && id.to_string() == item.element_id;
            previous = id;
            valid
        })
    });
    if reply.status != "inspected"
        || page.target != target
        || page.category != query.category
        || page.items.len() > query.limit as usize
        || page.scanned > 20_000
        || !valid_ids
        || page.next_after_id
            != page
                .items
                .last()
                .map(|r| r.element_id.as_str())
                .unwrap_or(&query.after_id)
        || page.has_more && page.items.is_empty()
    {
        return Err(error(
            "native_protocol",
            "Native inspection identity, cursor or page limits mismatch.",
            true,
        ));
    }
    Ok(page)
}
pub async fn health(client: &mut Client, request: HealthRequest) -> Result<Value, ApiError> {
    if !(1..=50).contains(&request.limit) {
        return Err(error(
            "invalid_arguments",
            "Health sample limit must be 1-50.",
            false,
        ));
    }
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(20);
    let mut checks = Vec::new();
    let mut coverage = Vec::new();
    for category in [
        Category::Views,
        Category::Sheets,
        Category::Doors,
        Category::Rooms,
        Category::Warnings,
    ] {
        let mut rows = Vec::new();
        let mut after_id = "0".to_string();
        let mut bytes = 0;
        let mut pages = 0;
        let complete;
        loop {
            if std::time::Instant::now() >= deadline || pages >= 100 {
                complete = false;
                break;
            }
            let page = elements(
                client,
                request.target.clone(),
                Query {
                    category,
                    after_id,
                    limit: 100,
                    name_contains: String::new(),
                    missing_mark: false,
                    ids: vec![],
                },
            )
            .await?;
            pages += 1;
            bytes += serde_json::to_vec(&page)
                .map_err(|_| error("native_protocol", "Invalid page data.", true))?
                .len();
            rows.extend(page.items);
            after_id = page.next_after_id;
            if !page.has_more || rows.len() >= 2_000 || bytes >= 2 * 1024 * 1024 {
                complete = page.scan_complete && !page.has_more;
                break;
            }
        }
        coverage.push(
            json!({"category":category,"examined":rows.len(),"complete":complete,"scan_limit":20000,
            "health_limit":2000,"pages":pages,"byte_budget":2097152,"time_budget_seconds":20}),
        );
        checks.extend(health::evaluate(
            category,
            &rows,
            complete,
            request.limit as usize,
        ));
    }
    Ok(
        json!({"target":request.target,"complete":checks.iter().all(|c| c.complete),"checks":checks,"coverage":coverage,
        "rules":"Review findings; no automatic fixes. Duplicate keys ignore case and edge whitespace; view names are compared within each Revit view kind. Counts are affected rows. Unplaced rooms have no location; unenclosed rooms are separate. All phases included; links and family documents excluded. Warnings use ordinal IDs with related element IDs. Templates and drawing sheets are excluded from duplicate view-name checks. No firm template policy is assumed."}),
    )
}
