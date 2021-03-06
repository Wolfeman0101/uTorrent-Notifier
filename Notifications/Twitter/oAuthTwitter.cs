using System;
using System.Data;
using System.Configuration;
using System.Web;
//using System.Web.Security;
using System.Net;
using System.IO;
using System.Collections.Specialized;

namespace uTorrentNotifier
{
    public class oAuthTwitter : OAuthBase
    {
        public enum Method { Get, Post };
        public const string RequestToken = "http://twitter.com/oauth/request_token";
        public const string Authorize = "http://twitter.com/oauth/authorize";
        public const string AccessToken = "http://twitter.com/oauth/access_token";

        private string _consumerKey = "";
        private string _consumerSecret = "";
        private string _token = "";
        private string _tokenSecret = "";
        private string _pin = ""; // JDevlin

        // JMD: this property should not have a dependency on the Settings file.
        public string ConsumerKey 
        {
            get
            {
                //if (_consumerKey == null || _consumerKey.Length == 0)
                //{
                //    _consumerKey = Settings1.Default.consumerKey;
                //}
                return _consumerKey; 
            } 
            set { _consumerKey = value; } 
        }
        
        // JMD: this property should not have a dependency on the Settings file.
        public string ConsumerSecret { 
            get {
                //if (_consumerSecret.Length == 0)
                //{
                //    _consumerSecret = Settings1.Default.consumerSecret;
                //}
                return _consumerSecret; 
            } 
            set { _consumerSecret = value; } 
        }

        public string OAuthToken { get; set; }
        public string Token { get { return _token; } set { _token = value; } }
        public string Pin { get { return _pin; } set { _pin = value; } }
        public string TokenSecret { get { return _tokenSecret; } set { _tokenSecret = value; } }

        /// <summary>
        /// Get the link to Twitter's authorization page for this application.
        /// </summary>
        /// <returns>The url with a valid request token, or a null string.</returns>
        public string AuthorizationLinkGet()
        {
            string ret = null;

            // First let's get a REQUEST token.
            string response = oAuthWebRequest(Method.Get, RequestToken, String.Empty);
            if (response.Length > 0)
            {
                //response contains token and token secret.  We only need the token.
                NameValueCollection qs = HttpUtility.ParseQueryString(response);
                if (qs["oauth_token"] != null)
                {
                    OAuthToken = qs["oauth_token"]; // tuck this away for later
                    ret = Authorize + "?oauth_token=" + qs["oauth_token"];// +"&oauth_callback=oob";
                }
            }
            return ret;
        }

        /// <summary>
        /// Exchange the request token for an access token.
        /// </summary>
        /// <param name="authToken">The oauth_token is supplied by Twitter's authorization page following the callback.</param>
        public void AccessTokenGet(string authToken, string pin)
        {
            this.Token = authToken;
            this._pin = pin; // JDevlin

            string response = oAuthWebRequest(Method.Get, AccessToken, String.Empty);

            if (response.Length > 0)
            {
                //Store the Token and Token Secret
                NameValueCollection qs = HttpUtility.ParseQueryString(response);
                if (qs["oauth_token"] != null)
                {
                    this.Token = qs["oauth_token"];
                }
                if (qs["oauth_token_secret"] != null)
                {
                    this.TokenSecret = qs["oauth_token_secret"];
                }
            }
        }
        
        /// <summary>
        /// Submit a web request using oAuth.
        /// </summary>
        /// <param name="method">GET or POST</param>
        /// <param name="url">The full url, including the querystring.</param>
        /// <param name="postData">Data to post (querystring format)</param>
        /// <returns>The web server response.</returns>
        public string oAuthWebRequest(Method method, string url, string postData)
        {
            string outUrl = "";
            string querystring = "";
            string ret = "";


            //Setup postData for signing.
            //Add the postData to the querystring.
            if (method == Method.Post)
            {
                if (postData.Length > 0)
                {
                    //Decode the parameters and re-encode using the oAuth UrlEncode method.
                    NameValueCollection qs = HttpUtility.ParseQueryString(postData);
                    postData = "";
                    foreach (string key in qs.AllKeys)
                    {
                        if (postData.Length > 0)
                        {
                            postData += "&";
                        }
                        qs[key] = HttpUtility.UrlDecode(qs[key]);
                        qs[key] = this.UrlEncode(qs[key]);
                        postData += key + "=" + qs[key];

                    }
                    if (url.IndexOf("?") > 0)
                    {
                        url += "&";
                    }
                    else
                    {
                        url += "?";
                    }
                    url += postData;
                }
            }
            else if (method == Method.Get && !String.IsNullOrEmpty(postData))
            {
                url += "?" + postData;
            }

            Uri uri = new Uri(url);
            
            string nonce = this.GenerateNonce();
            string timeStamp = this.GenerateTimestamp();

            //Generate Signature
            string sig = this.GenerateSignature(uri,
                this.ConsumerKey,
                this.ConsumerSecret,
                this.Token,
                this.TokenSecret,
                method.ToString(),
                timeStamp,
                nonce,
                this.Pin,
                out outUrl,
                out querystring);

            querystring += "&oauth_signature=" + HttpUtility.UrlEncode(sig);

            //Convert the querystring to postData
            if (method == Method.Post)
            {
                postData = querystring;
                querystring = "";
            }

            if (querystring.Length > 0)
            {
                outUrl += "?";
            }

            ret = WebRequest(method, outUrl +  querystring, postData);

            return ret;
        }

        /// <summary>
        /// Web Request Wrapper
        /// </summary>
        /// <param name="method">Http Method</param>
        /// <param name="url">Full url to the web resource</param>
        /// <param name="postData">Data to post in querystring format</param>
        /// <returns>The web server response.</returns>
        public string WebRequest(Method method, string url, string postData)
        {
            HttpWebRequest webRequest = null;
            StreamWriter requestWriter = null;
            string responseData = "";

            webRequest = System.Net.WebRequest.Create(url) as HttpWebRequest;
            webRequest.Method = method.ToString();
            webRequest.ServicePoint.Expect100Continue = false;
            //webRequest.UserAgent  = "Identify your application please.";
            //webRequest.Timeout = 20000;

            if (method == Method.Post)
            {
                webRequest.ContentType = "application/x-www-form-urlencoded";

                //POST the data.
                requestWriter = new StreamWriter(webRequest.GetRequestStream());
                try
                {
                    requestWriter.Write(postData);
                }
                catch
                {
                    throw;
                }
                finally
                {
                    requestWriter.Close();
                    requestWriter = null;
                }
            }

            responseData = WebResponseGet(webRequest);

            webRequest = null;

            return responseData;

        }

        /// <summary>
        /// Process the web response.
        /// </summary>
        /// <param name="webRequest">The request object.</param>
        /// <returns>The response data.</returns>
        public string WebResponseGet(HttpWebRequest webRequest)
        {
            StreamReader responseReader = null;
            string responseData = "";

            try
            {
                responseReader = new StreamReader(webRequest.GetResponse().GetResponseStream());
                responseData = responseReader.ReadToEnd();
            }
            catch
            {
                throw;
            }
            finally
            {
                webRequest.GetResponse().GetResponseStream().Close();
                responseReader.Close();
                responseReader = null;
            }

            return responseData;
        }

        // JMD: added for convenience. Reset the state of the oAuthTwitter object.
        public void Reset()
        {
            ConsumerKey = ConsumerSecret = OAuthToken = Token = TokenSecret = Pin = String.Empty;
        }
    }
}