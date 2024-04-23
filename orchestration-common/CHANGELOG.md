# Changelog

## v2.5.0

- Add "Type" field to error messages and helper messages. The goal is to have a uniform type/code to map to a message in the GUI for errors we want to show to the user.

## v2.4.0

### Features
- Add MQStreamProducer
- Fix MQStreamConsumer failing after stream is deleted
  - Consumes broker events (`queue.deleted`, `queue.created`) and restarts consumption from the beginning when the stream is recreated
  - Requires the `rabbitmq-event-exchange` plugin
- Add optional ConnectionName field to MQConsumer
- Refactor MQConsumer connection/channel initialization
- Add extension methods:
  - `IModel.StreamDeclare`
  - `IConnection.GetQueueInfo`
  - `IConnection.QueueExists`

## v2.3.0
- Add extension methods:
  - `byte[].Compress`
  - `byte[].Decompress` 
  - `byte[].Pack`

## v2.2.0
- Add extension methods:
  - `HttpResponseMessage.SetTimeout`
  - `HttpResponseMessage.GetTimeout`
- Add TimeoutHandler