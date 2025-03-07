﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Users;
using osuTK;

namespace osu.Game.Overlays.Dashboard.Friends
{
    public partial class FriendDisplay : CompositeDrawable
    {
        private List<APIUser> users = new List<APIUser>();

        public List<APIUser> Users
        {
            get => users;
            set
            {
                users = value;
                onlineStreamControl.Populate(value);
            }
        }

        private CancellationTokenSource cancellationToken;

        [CanBeNull]
        private SearchContainer currentContent;

        private FriendOnlineStreamControl onlineStreamControl;
        private Box background;
        private Box controlBackground;
        private UserListToolbar userListToolbar;
        private Container itemsPlaceholder;
        private LoadingLayer loading;
        private BasicSearchTextBox searchTextBox;

        private readonly IBindableList<APIRelation> apiFriends = new BindableList<APIRelation>();

        public FriendDisplay()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader(true)]
        private void load(OverlayColourProvider colourProvider, IAPIProvider api)
        {
            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Children = new Drawable[]
                        {
                            controlBackground = new Box
                            {
                                RelativeSizeAxes = Axes.Both
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Padding = new MarginPadding
                                {
                                    Top = 20,
                                    Horizontal = WaveOverlayContainer.HORIZONTAL_PADDING - FriendsOnlineStatusItem.PADDING
                                },
                                Child = onlineStreamControl = new FriendOnlineStreamControl(),
                            }
                        }
                    },
                    new Container
                    {
                        Name = "User List",
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Children = new Drawable[]
                        {
                            background = new Box
                            {
                                RelativeSizeAxes = Axes.Both
                            },
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Margin = new MarginPadding { Bottom = 20 },
                                Children = new Drawable[]
                                {
                                    new GridContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Padding = new MarginPadding
                                        {
                                            Horizontal = 40,
                                            Vertical = 20
                                        },
                                        ColumnDimensions = new[]
                                        {
                                            new Dimension(),
                                            new Dimension(GridSizeMode.Absolute, 50),
                                            new Dimension(GridSizeMode.AutoSize),
                                        },
                                        RowDimensions = new[]
                                        {
                                            new Dimension(GridSizeMode.AutoSize),
                                        },
                                        Content = new[]
                                        {
                                            new[]
                                            {
                                                searchTextBox = new BasicSearchTextBox
                                                {
                                                    RelativeSizeAxes = Axes.X,
                                                    Anchor = Anchor.CentreLeft,
                                                    Origin = Anchor.CentreLeft,
                                                    Height = 40,
                                                    ReleaseFocusOnCommit = false,
                                                    HoldFocus = true,
                                                    PlaceholderText = HomeStrings.SearchPlaceholder,
                                                },
                                                Empty(),
                                                userListToolbar = new UserListToolbar
                                                {
                                                    Anchor = Anchor.CentreRight,
                                                    Origin = Anchor.CentreRight,
                                                },
                                            },
                                        },
                                    },
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Children = new Drawable[]
                                        {
                                            itemsPlaceholder = new Container
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Padding = new MarginPadding { Horizontal = WaveOverlayContainer.HORIZONTAL_PADDING }
                                            },
                                            loading = new LoadingLayer(true)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            background.Colour = colourProvider.Background4;
            controlBackground.Colour = colourProvider.Background5;

            apiFriends.BindTo(api.Friends);
            apiFriends.BindCollectionChanged((_, _) => Schedule(() => Users = apiFriends.Select(f => f.TargetUser).ToList()), true);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            onlineStreamControl.Current.BindValueChanged(_ => recreatePanels());
            userListToolbar.DisplayStyle.BindValueChanged(_ => recreatePanels());
            userListToolbar.SortCriteria.BindValueChanged(_ => recreatePanels());
            searchTextBox.Current.BindValueChanged(_ =>
            {
                if (currentContent.IsNotNull())
                    currentContent.SearchTerm = searchTextBox.Current.Value;
            });
        }

        private void recreatePanels()
        {
            if (!users.Any())
                return;

            cancellationToken?.Cancel();

            if (itemsPlaceholder.Any())
                loading.Show();

            var sortedUsers = sortUsers(getUsersInCurrentGroup());

            LoadComponentAsync(createTable(sortedUsers), addContentToPlaceholder, (cancellationToken = new CancellationTokenSource()).Token);
        }

        private List<APIUser> getUsersInCurrentGroup()
        {
            switch (onlineStreamControl.Current.Value?.Status)
            {
                default:
                case OnlineStatus.All:
                    return users;

                case OnlineStatus.Offline:
                    return users.Where(u => !u.IsOnline).ToList();

                case OnlineStatus.Online:
                    return users.Where(u => u.IsOnline).ToList();
            }
        }

        private void addContentToPlaceholder(SearchContainer content)
        {
            loading.Hide();

            var lastContent = currentContent;

            if (lastContent != null)
            {
                lastContent.FadeOut(100, Easing.OutQuint).Expire();
                lastContent.Delay(25).Schedule(() => lastContent.BypassAutoSizeAxes = Axes.Y);
            }

            itemsPlaceholder.Add(currentContent = content);
            currentContent.FadeIn(200, Easing.OutQuint);
        }

        private SearchContainer createTable(List<APIUser> users)
        {
            var style = userListToolbar.DisplayStyle.Value;

            return new SearchContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Spacing = new Vector2(style == OverlayPanelDisplayStyle.Card ? 10 : 2),
                Children = users.Select(u => createUserPanel(u, style)).ToList(),
                SearchTerm = searchTextBox.Current.Value,
            };
        }

        private UserPanel createUserPanel(APIUser user, OverlayPanelDisplayStyle style)
        {
            switch (style)
            {
                default:
                case OverlayPanelDisplayStyle.Card:
                    return new UserGridPanel(user).With(panel =>
                    {
                        panel.Anchor = Anchor.TopCentre;
                        panel.Origin = Anchor.TopCentre;
                        panel.Width = 290;
                    });

                case OverlayPanelDisplayStyle.List:
                    return new UserListPanel(user);

                case OverlayPanelDisplayStyle.Brick:
                    return new UserBrickPanel(user);
            }
        }

        private List<APIUser> sortUsers(List<APIUser> unsorted)
        {
            switch (userListToolbar.SortCriteria.Value)
            {
                default:
                case UserSortCriteria.LastVisit:
                    return unsorted.OrderByDescending(u => u.LastVisit).ToList();

                case UserSortCriteria.Rank:
                    return unsorted.OrderByDescending(u => u.Statistics.GlobalRank.HasValue).ThenBy(u => u.Statistics.GlobalRank ?? 0).ToList();

                case UserSortCriteria.Username:
                    return unsorted.OrderBy(u => u.Username).ToList();
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            cancellationToken?.Cancel();
            base.Dispose(isDisposing);
        }
    }
}
