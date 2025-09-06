using AdaptiveCards.ObjectModel.WinUI3;
using AdaptiveCards.Rendering.WinUI3;
using Microsoft.UI.Xaml.Controls;


namespace CDMDevicesManagerDevWinUI.Views
{
    public sealed partial class Devices : Page
    {

        // Create json adaptive card string
        private string adaptiveCardJson = @"
        {
            ""type"": ""AdaptiveCard"",
            ""body"": [
                {
                    ""type"": ""TextBlock"",
                    ""text"": ""Hello, Adaptive Cards!"",
                    ""size"": ""Large"",
                    ""weight"": ""Bolder""
                }
            ],
            ""$schema"": ""http://adaptivecards.io/schemas/adaptive-card.json"",
            ""version"": ""1.3""
        }";


        public Devices()
        {
            this.InitializeComponent();
            var renderer = new AdaptiveCardRenderer();
            var card = AdaptiveCard.FromJsonString(adaptiveCardJson);
            RenderedAdaptiveCard renderedAdaptiveCard = renderer.RenderAdaptiveCard(card.AdaptiveCard);

            // Check if the render was successful
            if (renderedAdaptiveCard.FrameworkElement != null)
            {
                // Get the framework element
                var uiCard = renderedAdaptiveCard.FrameworkElement;

                // Add it to your UI listview or any other container
                

            }

        }

    }

}
