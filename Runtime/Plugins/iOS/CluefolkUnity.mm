#import <Cluefolk/Cluefolk-Swift.h>
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
extern "C" UIViewController *UnityGetGLViewController(void);
extern "C" void UnitySendMessage(const char *, const char *, const char *);
static NSString *receiver;
static id<CLFEventListener> eventListener;
static NSMutableDictionary<NSString *, void (^)(NSString *, NSError *)>
    *providers;
static NSString *Text(const char *value) {
  return value ? [NSString stringWithUTF8String:value] : @"";
}
static void Send(NSString *type, NSString *requestId, NSString *error,
                 NSString *payload) {
  NSDictionary *message = @{
    @"type" : type,
    @"requestId" : requestId ?: @"",
    @"error" : error ?: @"",
    @"payload" : payload ?: @"{}"
  };
  NSData *data = [NSJSONSerialization dataWithJSONObject:message
                                                 options:0
                                                   error:nil];
  NSString *json = [[NSString alloc] initWithData:data
                                         encoding:NSUTF8StringEncoding];
  UnitySendMessage(receiver.UTF8String, "OnNativeMessage", json.UTF8String);
}
@interface CLFUnityEventListener : NSObject <CLFEventListener>
@end

@implementation CLFUnityEventListener
- (void)onCluefolkReady { Send(@"ready", nil, nil, nil); }
- (void)onCluefolkOpened { Send(@"opened", nil, nil, nil); }
- (void)onCluefolkClosed { Send(@"closed", nil, nil, nil); }
- (void)onCluefolkUnreadChanged:(NSInteger)count {
  Send(@"unreadChanged", nil, nil,
       [NSString stringWithFormat:@"{\"count\":%ld}", (long)count]);
}
- (void)onCluefolkIdentityTokenRequired:(NSString *)requestId {
  Send(@"identityTokenRequired", requestId, nil, nil);
}
- (void)onCluefolkConversationCreated:(NSString *)conversationId {
  NSData *data = [NSJSONSerialization dataWithJSONObject:@{@"conversationId" : conversationId} options:0 error:nil];
  Send(@"conversationCreated", nil, nil, [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding]);
}
- (void)onCluefolkConversationStatusChanged:(NSString *)conversationId status:(NSString *)status {
  NSData *data = [NSJSONSerialization dataWithJSONObject:@{@"conversationId" : conversationId, @"status" : status} options:0 error:nil];
  Send(@"conversationStatusChanged", nil, nil, [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding]);
}
- (void)onCluefolkMessageSent:(NSString *)conversationId messageId:(NSString *)messageId {
  NSData *data = [NSJSONSerialization dataWithJSONObject:@{@"conversationId" : conversationId, @"messageId" : messageId} options:0 error:nil];
  Send(@"messageSent", nil, nil, [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding]);
}
- (void)onCluefolkError:(NSString *)code {
  NSData *data = [NSJSONSerialization dataWithJSONObject:@{@"code" : code}
                                                 options:0
                                                   error:nil];
  Send(@"error", nil, nil,
       [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding]);
}
@end

extern "C" void CLF_Initialize(const char *project, const char *api,
                               const char *logo, const char *gameObject) {
  NSString *p = Text(project), *a = Text(api), *l = Text(logo);
  receiver = Text(gameObject);
  dispatch_async(dispatch_get_main_queue(), ^{
    providers = [NSMutableDictionary dictionary];
    CLFCluefolk *sdk = [CLFCluefolk shared];
    eventListener = [[CLFUnityEventListener alloc] init];
    sdk.eventListener = eventListener;
    NSError *error = nil;
    [sdk initialize:[[CLFOptions alloc]
                        initWithProjectId:p
                                   apiURL:[NSURL URLWithString:a]
                            logoImageName:(l.length ? l : nil)]
              error:&error];
    if (error)
      Send(@"error", nil, @"INITIALIZATION_FAILED", nil);
  });
}
// Registered only when C# sets a provider: without one the SDK starts anonymously.
extern "C" void CLF_SetIdentityProvider(int enabled) {
  dispatch_async(dispatch_get_main_queue(), ^{
    if (!enabled) {
      [[CLFCluefolk shared] setIdentityTokenProvider:nil];
      return;
    }
    [[CLFCluefolk shared] setIdentityTokenProvider:^(void (^completion)(NSString *, NSError *)) {
      NSString *requestId = NSUUID.UUID.UUIDString;
      providers[requestId] = [completion copy];
      Send(@"identityTokenRequired", requestId, nil, nil);
    }];
  });
}
extern "C" void CLF_Login(const char *token, const char *requestId) {
  NSString *t = Text(token), *r = Text(requestId);
  dispatch_async(dispatch_get_main_queue(), ^{
    [[CLFCluefolk shared]
        loginWithIdentity:t
               completion:^(NSError *error) {
                 Send(@"completion", r, error ? @"IDENTITY_LOGIN_FAILED" : nil,
                      nil);
               }];
  });
}
extern "C" void CLF_Identity(const char *requestId, const char *token,
                             const char *error) {
  NSString *r = Text(requestId), *t = Text(token), *e = Text(error);
  dispatch_async(dispatch_get_main_queue(), ^{
    void (^completion)(NSString *, NSError *) = providers[r];
    [providers removeObjectForKey:r];
    if (completion)
      completion(t, e.length ? [NSError errorWithDomain:@"Cluefolk"
                                                   code:1
                                               userInfo:nil]
                             : nil);
    else
      // Requests raised through onCluefolkIdentityTokenRequired (no provider).
      [[CLFCluefolk shared] completeIdentityToken:r token:t error:e];
  });
}
extern "C" void CLF_Open(void) {
  dispatch_async(dispatch_get_main_queue(), ^{
    [[CLFCluefolk shared] openFrom:UnityGetGLViewController()];
  });
}
extern "C" void CLF_Language(const char *language) {
  NSString *value = Text(language);
  dispatch_async(dispatch_get_main_queue(), ^{ [[CLFCluefolk shared] setLanguage:value]; });
}
// Null or empty clears the game's player ID.
extern "C" void CLF_SetPlayerId(const char *playerId) {
  NSString *value = Text(playerId);
  dispatch_async(dispatch_get_main_queue(), ^{ [[CLFCluefolk shared] setPlayerId:value]; });
}
extern "C" int CLF_Push(const char *json, int opened) {
  NSData *data = [Text(json) dataUsingEncoding:NSUTF8StringEncoding];
  id value = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
  if (![value isKindOfClass:[NSDictionary class]] || ![value[@"origin"] isEqual:@"cluefolk"]) return 0;
  dispatch_async(dispatch_get_main_queue(), ^{
    if (opened) [[CLFCluefolk shared] openFromPush:value from:UnityGetGLViewController()];
    else [[CLFCluefolk shared] handlePush:value];
  });
  return 1;
}
extern "C" void CLF_OpenConversation(const char *id) {
  NSString *value = Text(id);
  dispatch_async(dispatch_get_main_queue(), ^{ [[CLFCluefolk shared] openConversation:value from:UnityGetGLViewController()]; });
}
extern "C" void CLF_Close(void) {
  dispatch_async(dispatch_get_main_queue(), ^{
    [[CLFCluefolk shared] close];
  });
}
extern "C" void CLF_Logout(void) {
  dispatch_async(dispatch_get_main_queue(), ^{
    [providers removeAllObjects];
    [[CLFCluefolk shared] logout];
  });
}
static void Values(const char *json, BOOL attributes) {
  NSData *data = [Text(json) dataUsingEncoding:NSUTF8StringEncoding];
  id value = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
  dispatch_async(dispatch_get_main_queue(), ^{
    NSError *error = nil;
    if (attributes)
      [[CLFCluefolk shared] setAttributes:value error:&error];
    else
      [[CLFCluefolk shared] setConversationContext:value error:&error];
    if (error)
      Send(@"error", nil, @"INVALID_CONTEXT", nil);
  });
}
// baseJson: the theme file (Settings → Widget → Download for build), empty for the default
// theme. overrides: the changes made in C#, a JSON object whose keys are all optional; both
// empty restore the default theme. A theme that does not build reports INVALID_APPEARANCE
// and the previous one stays.
extern "C" void CLF_SetAppearance(const char *baseJson, const char *overrides) {
  NSString *base = Text(baseJson), *changes = Text(overrides);
  dispatch_async(dispatch_get_main_queue(), ^{
    if (!base.length && !changes.length) {
      [[CLFCluefolk shared] setAppearance:nil];
      return;
    }
    NSError *error = nil;
    CLFAppearanceBuilder *builder =
        base.length ? [[CLFAppearanceBuilder alloc] initWithJSON:base error:&error]
                    : [[CLFAppearanceBuilder alloc] init];
    CLFAppearance *appearance = nil;
    if (builder && [builder applyOverridesJSON:changes error:&error])
      appearance = [builder build:&error];
    if (appearance)
      [[CLFCluefolk shared] setAppearance:appearance];
    else
      [[CLFCluefolk shared].eventListener onCluefolkError:@"INVALID_APPEARANCE"];
  });
}
extern "C" void CLF_Context(const char *json) { Values(json, NO); }
extern "C" void CLF_Attributes(const char *json) { Values(json, YES); }
extern "C" void CLF_Unset(const char *json) {
  id keys = [NSJSONSerialization
      JSONObjectWithData:[Text(json) dataUsingEncoding:NSUTF8StringEncoding]
                 options:0
                   error:nil];
  dispatch_async(dispatch_get_main_queue(), ^{
    NSError *error = nil;
    [[CLFCluefolk shared] unsetAttributes:keys error:&error];
    if (error)
      Send(@"error", nil, @"INVALID_ATTRIBUTES", nil);
  });
}
extern "C" void CLF_Unread(const char *requestId) {
  NSString *r = Text(requestId);
  dispatch_async(dispatch_get_main_queue(), ^{
    [[CLFCluefolk shared] getUnreadCount:^(NSInteger count, NSError *error) {
      Send(@"completion", r, error ? @"UNREAD_FAILED" : nil,
           [NSString stringWithFormat:@"{\"count\":%ld}", (long)count]);
    }];
  });
}
