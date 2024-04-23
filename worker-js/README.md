# JavaScript Worker

This worker receives the script in the message payload allowing it to be updated during runtime.

It uses Jint 3.x beta to parse and run the scripts which supports most features of ECMA Script 2015 (ES6). 
For a complete list refer to
 Jint's [readme](https://github.com/sebastienros/jint/#ecmascipt-features).

The script is expected to be included in the message payload. For example: 

```json
{
  "Payload": {
    "Details": {
      "Script": "function main(){ _println('hello world!'); }"
    },
    "Credentials": "{}"
  },
  "JobId": "ea5dec58-ddf8-45f5-a4db-6a662e3f6a15"
}
```

The entrypoint is the `main()` function that must be declared in the script.

A `cleanup()` function can be present. If present, it will be called after `main` finishes or in case an exception is thrown.

Refer to the example usage at the bottom for clarification.

# Built-in classes

## Request (`_request`)

Allows sending HTTP requests. 

This is a wrapper over [HttpClient](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpclient) and [HttpRequestMessage](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httprequestmessage).

> Note: Currently only supports sending string based content.


### Options

`_request.SetOptions(options: object)`

* `timeout`: Request timeout in seconds (default: 100)
* `mediaType`: default request MIME type (default: 'text/plain')
* `encoding`: default request encoding. (default: 'utf-8')
  * Use IANA preferred name (more info: [.NET documentation](https://docs.microsoft.com/en-us/dotnet/api/system.text.encoding.webname)) 


### Query parameters

* `_request.SetParams(parameters: object)`
* `_request.ClearParams()`

> Note: Parameters are reset after sending a request.

### Headers

* `_request.SetHeader(key: string, value: string)`
* `_request.RemoveHeader(key: string)`
* `_request.SetHeaders(headers: object)`
* `_request.ClearHeaders()`

> Note: Headers are reset after sending a request.
 
### Sending requests

* `_request.Get(url: string, body: object): object`
* `_request.Post(url: string, body: object): object`
* `_request.Put(url: string, body: object): object`
* `_request.Patch(url: string, body: object): object`
* `_request.Delete(url: string, body: object): object`

### Response

Sending a request will return a response object with the following model:

```json
{
  "text": "string content",
  "status": 200,
  "success": true
}
```

### Example
```js
const response_a = _request.Get('http://localhost:80/a', null);
_logger.info(response_a.text);

_request.SetHeader('Content-Type', 'application/json');
const response_b = _request.Post('http://localhost:80/b', {q: 'sample text'});
const result = response_b.text;
```

## Logger (`_logger`)

Wrapper for the .NET ILogger interface.

* `_logger.Trace(message: any)`
* `_logger.Debug(message: any)`
* `_logger.Info(message: any)`
* `_logger.Warn(message: any)`
* `_logger.Error(message: any)`
* `_logger.Critical(message: any)`

# Built-in Functions

Helper functions available from the global scope:

* `_println(message: string)` - wrapper over Console.WriteLine
* `_print(message: string)` - wrapper over Console.WriteLine

 
* `_delay(timeout: number)` - waits for the given time (in milliseconds)

 
* `atob(s: string): string` - decodes a base-64 encoded string
* `btoa(s: string): string` - encodes a string in base-64

# Globals

The following global constants are extracted from the message: 

* `_jobId` 
* `_message` the message payload
* `_credentials` (optional)

> Note: The `_message` does not include the script source.

# Example 

The embedded script uses TokenQueue to acquire the Gourmet MT worker for the language pair `en-bg` and sends several phrases for translation. When it is done the worker is released.

---
If the docker containers are not up run the following in the project root:

```shell
$ docker-compose -f ./docker-compose.yml up -d --force-recreate
```
---

Run the request shell script to orchestrate an example job graph used by the Javascript worker:

```shell
$ cd tokenQueue-example
$ ./request.sh
```

You can monitor the job progress using the logs for the `maestro-orchestration-worker-js-1` docker container.

Run the second script to see the results:
```shell
$ ./result.sh
```

**Script source:** [tokenQueue-script.js](tokenQueue-example/tokenQueue-script.js)