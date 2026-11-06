fn main() {
    let mut resource = winresource::WindowsResource::new();
    resource.set_icon("assets/eulord.ico");
    resource.set("ProductName", "One-Click EU Lord");
    resource.set(
        "FileDescription",
        "One-Click EU Lord - EU/EEA policy superset control terminal",
    );
    resource.set("CompanyName", "PZW152");
    resource.set("LegalCopyright", "MIT License");
    resource.set("FileVersion", "3.1.0.0");
    resource.set("ProductVersion", "3.1.0.0");
    resource
        .compile()
        .expect("failed to compile Windows resources");
}
