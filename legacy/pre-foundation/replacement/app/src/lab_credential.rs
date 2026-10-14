use std::{ffi::c_void, ptr};

#[repr(C)]
struct Credential {
    flags: u32,
    kind: u32,
    target: *mut u16,
    comment: *mut u16,
    last_written: u64,
    blob_size: u32,
    blob: *mut u8,
    persist: u32,
    attribute_count: u32,
    attributes: *mut c_void,
    alias: *mut u16,
    user_name: *mut u16,
}
#[link(name = "advapi32")]
unsafe extern "system" {
    fn CredReadW(target: *const u16, kind: u32, flags: u32, value: *mut *mut Credential) -> i32;
    fn CredFree(value: *mut c_void);
}
pub fn read(target: &str) -> Result<String, Box<dyn std::error::Error>> {
    let wide: Vec<u16> = target.encode_utf16().chain(Some(0)).collect();
    let mut entry = ptr::null_mut();
    // Only the exact validated lab credential target is read; no credential enumeration.
    if unsafe { CredReadW(wide.as_ptr(), 1, 0, &mut entry) } == 0 {
        return Err("Lab credential unavailable. Start the approved lab session first.".into());
    }
    let result = unsafe {
        if (*entry).blob_size != 64 || (*entry).blob.is_null() {
            Err("Invalid lab credential".into())
        } else {
            let bytes = std::slice::from_raw_parts((*entry).blob, 64);
            if bytes.iter().all(u8::is_ascii_hexdigit) {
                Ok(String::from_utf8(bytes.to_vec())?)
            } else {
                Err("Invalid lab credential".into())
            }
        }
    };
    unsafe { CredFree(entry.cast()) };
    result
}
