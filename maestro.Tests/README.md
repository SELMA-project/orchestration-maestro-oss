# maestro Tests

The following configuration files will be used if present:

- `.\appsettings.test.json`
- `..\maestro\appsettings.json`

## Config options

### `appsettings.test.json`

#### Default values

```json
{
  "Tests": {
    "Docker": {
      "ProjectName": "maestro-tests",
      "BuildImages": true,
      "ForceRecreate": true
    }
  }
}
```

- ProjectName - separate project for test containers; easily inspect the logs/db after testing
- BuildImages - pass the `--build` switch to `docker-compose up`
- ForceRecreate - pass the `--force-recreate` switch to `docker-compose up`

You can tweak these if the tests fail to send an HTTP request, or if you don't need to rebuild the images and want to make the tests run faster.

### `..\maestro\appsettings.json`

- The test fixture will wait for 2 seconds + `RabbitMQ:ConnectionRetryIn` to give maestro time to connect to RabbitMQ
- A new test queue is bound to the exchange defined in `Integration:Exchanges:Workers.Out,`

## Logging

To run with messages: `dotnet test --logger "console;verbosity=detailed"`

### VS Code

If using the [.NET Test Explorer](vscode:extension/formulahendry.dotnet-test-explorer) extension, update your config to show the log in the Output tab (Test Explorer category):

```json
  "dotnet-test-explorer.testArguments": "--logger \"console;verbosity=detailed\""
```

> **Note:** You have to wait until all tests end for the diagnostic messages to appear in the log. </br> To see them in real time run `dotnet test` in a Terminal as described above.
