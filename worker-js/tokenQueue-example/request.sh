#!/bin/bash
GRAPH=$(
  cat <<EOF
{
  "workflowId": "94bfda4e-b0dd-44fb-8b9c-9264f073bf14",
  "jobNodes": [
    {
      "id": "9e284de5-4a99-4082-8efe-da531a41a47b",
      "dependencies": [],
      "jobType": "JavaScript",
      "jobProvider": "Selma",
      "jobData": {
        "SegmentedTranscript": {
          "Segments": [
            {
              "BlockId": "197a504e-33bb-4b71-b354-935c86ba29a5",
              "Start": 1.6,
              "End": 2.61,
              "Text": "This is DW news."
            },
            {
              "BlockId": "197a504e-33bb-4b71-b354-935c86ba29a5",
              "Start": 2.62,
              "End": 4.37,
              "Text": "These are our top stories."
            },
            {
              "BlockId": "197a504e-33bb-4b71-b354-935c86ba29a5",
              "Start": 4.82,
              "End": 7.95,
              "Text": "Ethiopians have been voting in‧general elections that are seen as"
            },
            {
              "BlockId": "197a504e-33bb-4b71-b354-935c86ba29a5",
              "Start": 7.96,
              "End": 11.49,
              "Text": "the first major test of Prime‧Minister Abbey Ahmed and his ruling"
            },
            {
              "BlockId": "197a504e-33bb-4b71-b354-935c86ba29a5",
              "Start": 11.6,
              "End": 16.29,
              "Text": "coalition violence and logistical‧problems cause delays in four of"
            },
            {
              "BlockId": "197a504e-33bb-4b71-b354-935c86ba29a5",
              "Start": 16.3,
              "End": 17.75,
              "Text": "the country's 10 regions."
            }
          ]
        },
        "BlockeredTranscript": null,
        "SingleSegment": null,
        "Credentials": "{}",
        "ServiceVariant": "Default",
        "SourceLangCode": "en",
        "TargetLangCode": "bg",
        "Details": {
        "Script": "const token_queue_url = 'http://localhost:9000/api/workers';
let url;

const objectFilter = (obj, callback) => Object.fromEntries(Object.entries(obj).filter(callback));

const isValueValid = ([, v]) => v !== undefined;

function add_worker(url, type, engine, version) {
    let body = objectFilter({'type': type, 'engine': engine, 'version': version}, isValueValid);

    _logger.debug('parsed values: ' + JSON.stringify(body));

    const response = _request.Post(token_queue_url, body);
    return response.text;
}

function acquire_worker(type, engine, version) {
    let params = objectFilter({'type': type, 'engine': engine, 'version': version}, isValueValid);

    _logger.debug('parsed values: ' + JSON.stringify(params));

    _request.SetParams(params);
    _request.SetHeader('Content-Type', 'application/json');
    const response = _request.Get(token_queue_url + '/acquire', null);
    return response.text;
}

function release_worker(url) {
    const response = _request.Post(token_queue_url + '/release', url);
    return response.success;
}

function gourmet_mt(url, text) {
    _logger.debug('translating text: ' + text);
    _request.SetHeader('Content-Type', 'application/json');
    let response = _request.Post(url, {q: text});
    if (!response.success) {
        throw new Error(response.error);
    }
    return JSON.parse(response.text).result;
}

function main() {
    const engine = _message.SourceLangCode + '-' + _message.TargetLangCode;
    url = acquire_worker('gourmet-mt', engine);

    let result = {'BlockeredTranscript': null, 'SingleSegment': null}
    
    if (_message.SegmentedTranscript !== null) {
        const segments = _message.SegmentedTranscript.Segments;
        for (const segment of segments) {
            segment.Text = gourmet_mt(url, segment.Text);
        }
        result.BlockeredTranscript = {'Blocks': segments};
    } else if(_message.SingleSegment !== null){
        const text = _message.SingleSegment;
        result.SingleSegment = gourmet_mt(url, text);
    } else if(_message.BlockeredTranscript !== null){
        throw new Error('BlockeredTranscript not implemented!');
    }
    
    _logger.debug('results:' + JSON.stringify(result));
    return result;
}

function cleanup() {
    release_worker(url);
}
"
        }
      }
    }
  ]
}
EOF
)
curl -X POST "http://localhost:10000/Orchestration/Graph" -H "accept: text/plain" -H "Content-Type: application/json" -d "$GRAPH"
