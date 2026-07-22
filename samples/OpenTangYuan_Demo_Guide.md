# OpenTangYuan Local Workflow Demo

This demo shows how **OpenTangYuan Runtime** loads and runs a local workflow stored in the database.

The workflow searches for a file, copies it to an output folder, and then opens the copied file.

> [!IMPORTANT]
> **You do not need to install .NET 8.**
>
> The Release package already includes the required .NET Runtime. Extract the ZIP file first, then start the demo with `Start-Demo.bat` from the root folder.

## Screenshot

![OpenTangYuan Demo](demo.png)

## Quick Start

### Requirements

- Windows 10 or 11 (x64)
- No .NET SDK required
- No .NET Runtime installation required
- Administrator permissions are not required

### How to Run

1. Download the Demo ZIP from GitHub Releases.
2. Extract the entire ZIP file to a local folder, for example:

   ```text
   C:\OpenTangYuan-Demo
   ```

3. Double-click the following file in the root folder:

   ```text
   Start-Demo.bat
   ```

4. In the demo window, click the buttons in this order:

   ```text
   Start Runtime
         ↓
   Quick Check
         ↓
   Run Demo
   ```

> [!WARNING]
> Do not start the demo by double-clicking `WinForm\TangYuan.Demo.exe`.
>
> The demo uses the private .NET 8 runtime included in the package. `Start-Demo.bat` sets up the required runtime environment before launching the application.

## What the Demo Does

The workflow used by this demo is already stored in the database and is loaded with the following `SkillCode`:

```text
demo_local_file_workflow
```

The workflow runs these steps:

```text
Find sample-report.txt
        ↓
Copy it to demo-output
        ↓
Open the copied file
```

This flow verifies that OpenTangYuan can:

- Load a saved workflow from the database
- Run a workflow by `SkillCode`
- Pass runtime arguments into the workflow
- Pass data from one workflow step to the next
- Use local file search, copy, and open actions
- Confirm that the output file was actually created

## Folder Structure

```text
DemoExe
├── Start-Demo.bat       # Demo launcher
├── runtime              # Bundled .NET runtime
├── serverExe            # OpenTangYuan Runtime
│   └── TangYuan.exe
└── WinForm              # Desktop demo application
    ├── TangYuan.Demo.exe
    └── sample-report.txt
```

Keep this folder structure unchanged.

Do not:

- Move `TangYuan.Demo.exe` by itself
- Move `TangYuan.exe` by itself
- Delete or move the `runtime`, `serverExe`, or `WinForm` folders
- Run the demo directly from inside the ZIP file
- Delete configuration files, dependency DLLs, `.runtimeconfig.json`, or `.deps.json` files

## Expected Result

After you click **Run Demo**, the application will:

1. Check that the Runtime is running.
2. Load the workflow from the database.
3. Search for, copy, and open the sample file.
4. Create the following file:

   ```text
   WinForm\demo-output\sample-report-copy.txt
   ```

5. Open the copied file with the default application on your system.

If the copied file opens successfully, the demo has completed as expected.

## Troubleshooting

### The demo does not start

Make sure the ZIP file has been fully extracted, then start the demo with:

```text
Start-Demo.bat
```

Do not run:

```text
WinForm\TangYuan.Demo.exe
```

### The Runtime does not start

Check that:

- `serverExe\TangYuan.exe` exists
- Port `54124` is not being used by another application

### sample-report.txt cannot be found

Make sure the file is located at:

```text
WinForm\sample-report.txt
```

### The output file cannot be replaced

Close the previously opened file:

```text
sample-report-copy.txt
```

Then run the demo again.

## More Information

This README only covers how to run the demo.

For the full project overview, workflow definitions, APIs, Runtime architecture, and implementation details, see the main `README.md` and the documentation in the project root.
